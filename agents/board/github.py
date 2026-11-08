"""The board on GitHub: Issues for requests, epics and tasks; a Projects board for columns and fields.
Every call goes through the GitHub CLI (`gh`), so it uses the designer's GitHub login."""
import json
import shutil
import subprocess
import time

from .model import STATUSES, Comment, Issue, Item, attempt_label, attempt_number

ITEMS_QUERY = """
query($id: ID!, $after: String) {
  node(id: $id) { ... on ProjectV2 {
    items(first: 100, after: $after) {
      pageInfo { hasNextPage endCursor }
      nodes {
        id
        content { ... on Issue { number title state labels(first: 10) { nodes { name } } } }
        fieldValues(first: 20) { nodes {
          ... on ProjectV2ItemFieldSingleSelectValue { name field { ... on ProjectV2FieldCommon { name } } }
          ... on ProjectV2ItemFieldNumberValue { number field { ... on ProjectV2FieldCommon { name } } }
        } }
      }
    }
  } }
}"""

UPDATE_OPTIONS = """
mutation($id: ID!, $opts: [ProjectV2SingleSelectFieldOptionInput!]) {
  updateProjectV2Field(input: {fieldId: $id, singleSelectOptions: $opts}) {
    projectV2Field { ... on ProjectV2SingleSelectField { id } }
  }
}"""

STATUS_COLORS = ["GRAY", "BLUE", "YELLOW", "ORANGE", "PURPLE", "GREEN", "RED"]


class GhError(RuntimeError):
    pass


def gh(*args, stdin=None):
    exe = shutil.which("gh")
    if not exe:
        raise GhError("GitHub CLI not found. Install it: winget install GitHub.cli, then: gh auth login")
    res = subprocess.run([exe, *args], input=stdin, capture_output=True, text=True, encoding="utf-8")
    if res.returncode != 0:
        raise GhError(f"gh {' '.join(args[:3])} failed: {(res.stderr or res.stdout).strip()[:600]}")
    return res.stdout.strip()


def gh_json(*args, stdin=None):
    out = gh(*args, stdin=stdin)
    return json.loads(out) if out else None


def graphql(query, **variables):
    body = json.dumps({"query": query, "variables": variables})
    data = gh_json("api", "graphql", "--input", "-", stdin=body)
    if data.get("errors"):
        raise GhError(f"GraphQL: {data['errors'][0].get('message')}")
    return data["data"]


class GitHubBoard:
    def __init__(self, settings):
        self.s = settings
        self.repo = settings["repo"]
        self.owner = settings["project_owner"]
        self.title = settings["project_title"]
        self._number = None
        self._fields = None
        self._item_ids = {}
        self._project_id = None

    # ------------------------------------------------------------ project plumbing

    def _find(self):
        data = gh_json("project", "list", "--owner", self.owner, "--format", "json", "--limit", "100")
        for p in data.get("projects", []):
            if p["title"] == self.title:
                self._number, self._project_id = p["number"], p["id"]
                return True
        return False

    @property
    def number(self):
        if self._number is None and not self._find():
            raise GhError(f"No board named '{self.title}' yet: run  python agents/crew.py setup")
        return self._number

    @property
    def project_id(self):
        if not self._project_id:
            _ = self.number
        return self._project_id

    def fields(self):
        if self._fields is None:
            data = gh_json("project", "field-list", str(self.number), "--owner", self.owner, "--format", "json")
            self._fields = {f["name"]: f for f in data["fields"]}
        return self._fields

    def _option(self, field, name):
        f = self.fields().get(field)
        if not f:
            raise GhError(f"Board field '{field}' is missing: run  python agents/crew.py setup")
        for o in f.get("options", []):
            if o["name"] == name:
                return f["id"], o["id"]
        raise GhError(f"Board field '{field}' has no option '{name}': run  python agents/crew.py setup")

    def _item(self, number):
        if number not in self._item_ids:
            self.items()
        if number not in self._item_ids:
            raise GhError(f"#{number} is not on the board")
        return self._item_ids[number]

    # ------------------------------------------------------------ reading

    def items(self):
        out, after = [], None
        while True:
            page = graphql(ITEMS_QUERY, id=self.project_id, after=after)["node"]["items"]
            for n in page["nodes"]:
                c = n.get("content") or {}
                if "number" not in c:
                    continue  # a draft item, not an issue
                labels = [l["name"] for l in c["labels"]["nodes"]]
                kind = "epic" if "epic" in labels else "task" if "task" in labels else None
                if not kind:
                    continue
                vals = {v["field"]["name"]: v.get("name", v.get("number"))
                        for v in n["fieldValues"]["nodes"] if v and v.get("field")}
                self._item_ids[c["number"]] = n["id"]
                out.append(Item(number=c["number"], title=c["title"], kind=kind, status=vals.get("Status") or "",
                                attempt=attempt_number(vals.get("Attempt")), tokens=int(vals.get("Used tokens") or 0),
                                state=c["state"], item_id=n["id"]))
            if not page["pageInfo"]["hasNextPage"]:
                return out
            after = page["pageInfo"]["endCursor"]

    @staticmethod
    def _issue(d):
        return Issue(number=d["number"], title=d["title"], body=d.get("body") or "",
                     labels=[l["name"] for l in d.get("labels", [])], state=d["state"], url=d.get("url", ""),
                     comments=[Comment(c["body"], c.get("createdAt", "")) for c in d.get("comments", [])])

    def requests(self):
        data = gh_json("issue", "list", "--repo", self.repo, "--label", "request", "--state", "open", "--limit", "100",
                       "--json", "number,title,body,labels,state,url,comments")
        return sorted((self._issue(d) for d in data), key=lambda i: i.number)

    def issue(self, number):
        return self._issue(gh_json("issue", "view", str(number), "--repo", self.repo,
                                   "--json", "number,title,body,labels,state,url,comments"))

    # ------------------------------------------------------------ writing

    def create_issue(self, title, body, labels):
        args = ["issue", "create", "--repo", self.repo, "--title", title, "--body-file", "-"]
        for l in labels:
            args += ["--label", l]
        url = gh(*args, stdin=body)
        return int(url.rstrip("/").split("/")[-1])

    def add_sub_issue(self, parent, child):
        try:
            child_id = gh("api", f"repos/{self.repo}/issues/{child}", "--jq", ".id")
            gh("api", "-X", "POST", f"repos/{self.repo}/issues/{parent}/sub_issues", "-F", f"sub_issue_id={child_id}")
        except GhError as e:  # the card still names its epic, so the runner works without sub-issues
            print(f"  (could not link #{child} as a sub-issue of #{parent}: {e})")

    def add_item(self, number, status, attempt=None):
        url = f"https://github.com/{self.repo}/issues/{number}"
        try:
            item = gh_json("project", "item-add", str(self.number), "--owner", self.owner, "--url", url,
                           "--format", "json")
            self._item_ids[number] = item["id"]
        except GhError as e:
            if "already exists" not in str(e):
                raise
            for _ in range(10):  # GitHub added it already; its item can take a few seconds to show up
                self.items()
                if number in self._item_ids:
                    break
                time.sleep(2)
            else:
                raise
        self.set_status(number, status)
        if attempt is not None:
            self.set_attempt(number, attempt)

    def _edit(self, issue, field_id, flag, value):
        gh("project", "item-edit", "--id", self._item(issue), "--project-id", self.project_id,
           "--field-id", field_id, f"--{flag}", str(value))

    def set_status(self, number, status):
        fid, oid = self._option("Status", status)
        self._edit(number, fid, "single-select-option-id", oid)

    def set_attempt(self, number, n):
        fid, oid = self._option("Attempt", attempt_label(n, self.s["max_attempts"]))
        self._edit(number, fid, "single-select-option-id", oid)

    def set_tokens(self, number, total):
        f = self.fields().get("Used tokens")
        if not f:
            raise GhError("Board field 'Used tokens' is missing: run  python agents/crew.py setup")
        self._edit(number, f["id"], "number", total)

    def comment(self, number, body):
        gh("issue", "comment", str(number), "--repo", self.repo, "--body-file", "-", stdin=body)

    def close(self, number, reason="completed"):
        gh("issue", "close", str(number), "--repo", self.repo, "--reason", reason)

    def pr_for(self, branch):
        prs = gh_json("pr", "list", "--repo", self.repo, "--head", branch, "--state", "all", "--json", "url,state")
        return prs[0]["url"] if prs else None

    def open_pr(self, branch, base, title, body):
        return gh("pr", "create", "--repo", self.repo, "--base", base, "--head", branch, "--title", title,
                  "--body-file", "-", stdin=body)

    def link(self, number):
        return f"https://github.com/{self.repo}/issues/{number}"

    # ------------------------------------------------------------ one-time setup

    def setup(self):
        from .model import LABELS
        print("Labels")
        for name, (color, desc) in LABELS.items():
            gh("label", "create", name, "--repo", self.repo, "--color", color, "--description", desc, "--force")
            print(f"  {name}")
        if not self._find():
            data = gh_json("project", "create", "--owner", self.owner, "--title", self.title, "--format", "json")
            self._number, self._project_id = data["number"], data["id"]
            print(f"Board created: {data.get('url', '')}")
        else:
            print(f"Board found: '{self.title}' (#{self._number})")
        try:
            gh("project", "link", str(self.number), "--owner", self.owner, "--repo", self.repo)
        except GhError:
            pass  # already linked
        fields = self.fields()
        status = fields.get("Status")
        names = [o["name"] for o in status.get("options", [])] if status else []
        if names != STATUSES:
            opts = [{"name": s, "color": c, "description": ""} for s, c in zip(STATUSES, STATUS_COLORS)]
            try:
                graphql(UPDATE_OPTIONS, id=status["id"], opts=opts)
                print("Status columns set: " + ", ".join(STATUSES))
            except (GhError, TypeError) as e:
                print(f"Could not set the Status columns automatically ({e}).\n"
                      f"  On the board: open the Status field's settings and make its options exactly: "
                      + ", ".join(STATUSES))
        attempts = [attempt_label(i, self.s["max_attempts"]) for i in range(self.s["max_attempts"] + 1)]
        if "Attempt" not in fields:
            gh("project", "field-create", str(self.number), "--owner", self.owner, "--name", "Attempt",
               "--data-type", "SINGLE_SELECT", "--single-select-options", ",".join(attempts))
            print("Field Attempt created")
        if "Used tokens" not in fields:
            gh("project", "field-create", str(self.number), "--owner", self.owner, "--name", "Used tokens",
               "--data-type", "NUMBER")
            print("Field Used tokens created")
        try:
            gh("api", "-X", "PATCH", f"repos/{self.repo}", "-F", "delete_branch_on_merge=true")
            print("Repo setting: automatically delete head branches")
        except GhError as e:
            print(f"Turn on 'Automatically delete head branches' in the repo settings yourself ({e})")
        self._fields = None
