"""A stand-in for the GitHub board, kept in one JSON file, for --mock runs without GitHub."""
import json
import pathlib

from .model import Comment, Issue, Item


class LocalBoard:
    def __init__(self, path, max_attempts):
        self.path = pathlib.Path(path)
        self.max_attempts = max_attempts
        self.d = json.loads(self.path.read_text(encoding="utf-8")) if self.path.exists() else \
            {"next": 1, "issues": {}, "items": {}, "prs": {}}

    def _save(self):
        self.path.write_text(json.dumps(self.d, indent=2), encoding="utf-8", newline="\n")

    def _issue(self, n):
        i = self.d["issues"][str(n)]
        return Issue(number=n, title=i["title"], body=i["body"], labels=i["labels"], state=i["state"],
                     comments=[Comment(c) for c in i["comments"]], url=self.link(n))

    def requests(self):
        return [self._issue(int(n)) for n, i in self.d["issues"].items()
                if "request" in i["labels"] and i["state"] == "OPEN"]

    def items(self):
        out = []
        for n, it in self.d["items"].items():
            i = self.d["issues"][n]
            out.append(Item(number=int(n), title=i["title"], kind="epic" if "epic" in i["labels"] else "task",
                            status=it["status"], attempt=it.get("attempt", 0), tokens=it.get("tokens", 0),
                            state=i["state"]))
        return out

    def issue(self, number):
        return self._issue(number)

    def create_issue(self, title, body, labels):
        n = self.d["next"]
        self.d["next"] += 1
        self.d["issues"][str(n)] = {"title": title, "body": body, "labels": labels, "state": "OPEN", "comments": []}
        self._save()
        return n

    def set_body(self, number, body):
        self.d["issues"][str(number)]["body"] = body
        self._save()

    def add_sub_issue(self, parent, child):
        pass

    def add_item(self, number, status, attempt=None):
        self.d["items"][str(number)] = {"status": status, "attempt": attempt or 0, "tokens": 0}
        self._save()

    def set_status(self, number, status):
        self.d["items"][str(number)]["status"] = status
        self._save()

    def set_attempt(self, number, n):
        self.d["items"][str(number)]["attempt"] = n
        self._save()

    def set_tokens(self, number, total):
        self.d["items"][str(number)]["tokens"] = total
        self._save()

    def comment(self, number, body):
        self.d["issues"][str(number)]["comments"].append(body)
        self._save()

    def close(self, number, reason="completed"):
        self.d["issues"][str(number)]["state"] = "CLOSED"
        self._save()

    def pr_for(self, branch):
        return self.d["prs"].get(branch)

    def open_pr(self, branch, base, title, body):
        url = f"local-pr://{branch}"
        self.d["prs"][branch] = url
        self._save()
        return url

    def link(self, number):
        return f"local://issues/{number}"
