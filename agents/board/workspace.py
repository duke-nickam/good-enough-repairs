"""Where agents' files land: the repo on the designer's PC, one branch per epic (GitWorkspace),
or a temporary folder for --mock runs (MockWorkspace)."""
import pathlib
import subprocess

SKIP_LIST = ("Assets/Settings/", "ProjectSettings/", "Packages/", "UserSettings/")


class WorkspaceError(RuntimeError):
    pass


class GitWorkspace:
    def __init__(self, root, repo):
        self.root = pathlib.Path(root)
        self.repo = repo
        self.home_branch = None
        self.base = None

    def git(self, *args, check=True):
        res = subprocess.run(["git", *args], cwd=self.root, capture_output=True, text=True, encoding="utf-8")
        if check and res.returncode != 0:
            raise WorkspaceError(f"git {' '.join(args[:3])} failed: {(res.stderr or res.stdout).strip()[:500]}")
        return res.stdout.strip()

    def start(self):
        if self.git("status", "--porcelain", "--untracked-files=no"):
            raise WorkspaceError("The repo has uncommitted changes. Commit or discard them first, "
                                 "so the agents' commits stay clean.")
        self.home_branch = self.git("rev-parse", "--abbrev-ref", "HEAD")
        self.git("fetch", "origin", "--prune")
        head = self.git("symbolic-ref", "refs/remotes/origin/HEAD", check=False)
        self.base = head.split("/")[-1] if head else "main"

    def finish(self):
        if self.home_branch and self.git("rev-parse", "--abbrev-ref", "HEAD") != self.home_branch:
            self.git("checkout", self.home_branch)

    def use_branch(self, branch):
        if self.git("rev-parse", "--abbrev-ref", "HEAD") == branch:
            return
        local = self.git("branch", "--list", branch)
        remote = self.git("ls-remote", "--heads", "origin", branch)
        if local:
            self.git("checkout", branch)
            if remote:
                self.git("pull", "--ff-only", "origin", branch)
        elif remote:
            self.git("checkout", "-b", branch, f"origin/{branch}")
        else:
            self.git("checkout", "-b", branch, f"origin/{self.base}")

    def read(self, path):
        p = self.root / path
        return p.read_text(encoding="utf-8") if p.is_file() else None

    def write(self, path, content):
        p = self.root / path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(content, encoding="utf-8", newline="\n")

    def commit(self, paths, message):
        self.git("add", "--", *paths)
        if not self.git("diff", "--cached", "--name-only"):
            return None  # nothing changed
        self.git("commit", "-m", message)
        sha = self.git("rev-parse", "HEAD")
        branch = self.git("rev-parse", "--abbrev-ref", "HEAD")
        self.git("push", "-u", "origin", branch)
        return sha

    def commit_link(self, sha):
        return f"https://github.com/{self.repo}/commit/{sha}"

    def has_commits(self, branch):
        remote = self.git("ls-remote", "--heads", "origin", branch)
        if not remote:
            return False
        return self.git("rev-list", "--count", f"origin/{self.base}..origin/{branch}", check=False) not in ("", "0")

    def list_files(self):
        files = self.git("ls-files").splitlines()
        return "\n".join(f for f in files if not f.endswith(".meta") and not f.startswith(SKIP_LIST))


class MockWorkspace:
    """Reads from the real repo, writes into a temporary folder; commits are only logged."""

    def __init__(self, repo_root, scratch):
        self.repo_root = pathlib.Path(repo_root)
        self.root = pathlib.Path(scratch)
        self.base = "main"
        self.n = 0
        self.branches = set()

    def start(self):
        pass

    def finish(self):
        pass

    def use_branch(self, branch):
        self.branches.add(branch)

    def read(self, path):
        for base in (self.root, self.repo_root):
            p = base / path
            if p.is_file():
                return p.read_text(encoding="utf-8")
        return None

    def write(self, path, content):
        p = self.root / path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(content, encoding="utf-8", newline="\n")

    def commit(self, paths, message):
        self.n += 1
        return f"mock{self.n:04d}"

    def commit_link(self, sha):
        return f"(commit {sha})"

    def has_commits(self, branch):
        return branch in self.branches

    def list_files(self):
        return "Assets/Data/economy.json\nAssets/Scripts/Data/EconomyConfig.cs\nAssets/Scripts/Data/EconomyLoader.cs"
