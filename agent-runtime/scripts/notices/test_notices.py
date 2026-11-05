import contextlib
import http.client
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))

import notices  # noqa: E402

NUSPEC = """<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{name}</id>
    <version>{version}</version>
    {licence}
  </metadata>
</package>
"""


class Tree:
    def __init__(self, root: Path) -> None:
        self.root = root
        self.lock: dict[str, dict] = {"": {"name": "app"}}
        self.policy: dict = {"sources": [{"type": "npm", "path": "package-lock.json"}], "allowed": ["MIT", "Apache-2.0"]}
        self.write("THIRD-PARTY-NOTICES.md", f"# Third-party notices\n\n{notices.START}\n{notices.END}\n")
        self.save()

    def write(self, path: str, text: str) -> None:
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")

    def npm(self, name: str, version: str, licence: str) -> None:
        self.lock[f"node_modules/{name}"] = {"version": version, "license": licence}
        self.save()

    def nuget(self, name: str, version: str, licence: str, file_text: str | None = None, file_path: str = "LICENSE") -> None:
        self.write(f"packages/{name.lower()}/{version}/{name.lower()}.nuspec", NUSPEC.format(name=name, version=version, licence=licence))
        if file_text is not None:
            self.write(f"packages/{name.lower()}/{version}/{file_path}", file_text)
        assets = {
            "targets": {"net10.0": {f"{name}/{version}": {"type": "package"}}, "net10.0/osx-arm64": {f"{name}/{version}": {"type": "package"}, "runtime.osx-arm64.Native/1.0.0": {"type": "package"}}},
            "libraries": {f"{name}/{version}": {"type": "package", "path": f"{name.lower()}/{version}"}, "runtime.osx-arm64.Native/1.0.0": {"type": "package", "path": "runtime.osx-arm64.native/1.0.0"}},
            "packageFolders": {str(self.root / "packages"): {}},
        }
        self.write(f"artifacts/obj/{name}/project.assets.json", json.dumps(assets))

    def set_policy(self, **entries: object) -> None:
        self.policy.update(entries)
        self.save()

    def save(self) -> None:
        self.write("package-lock.json", json.dumps({"lockfileVersion": 3, "packages": self.lock}))
        self.write("scripts/notices/policy.json", json.dumps(self.policy))

    def run(self, command: str) -> tuple[int, str]:
        errors = io.StringIO()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(errors):
            code = notices.main([command, "--root", str(self.root)])
        return code, errors.getvalue()

    def notices(self) -> str:
        return (self.root / "THIRD-PARTY-NOTICES.md").read_text()


class ExpressionTests(unittest.TestCase):
    def allowed(self, text: str) -> bool:
        expression = notices.parse_expression(text)
        self.assertIsNotNone(expression, text)
        return notices.satisfies(expression, {"MIT", "Apache-2.0", "GPL-2.0-only WITH Classpath-exception-2.0"})

    def test_or_needs_one_allowed_licence_and_and_needs_all(self) -> None:
        self.assertTrue(self.allowed("(GPL-3.0-or-later OR MIT)"))
        self.assertFalse(self.allowed("MIT AND CC-BY-4.0"))
        self.assertTrue(self.allowed("MIT AND (Apache-2.0 OR GPL-3.0-only)"))

    def test_with_names_one_licence(self) -> None:
        self.assertTrue(self.allowed("GPL-2.0-only WITH Classpath-exception-2.0"))
        self.assertFalse(self.allowed("GPL-2.0-only"))

    def test_prose_and_pointers_are_not_expressions(self) -> None:
        for text in ["MIT License", "SEE LICENSE IN LICENSE.md", "url https://example.com/LICENSE", notices.NO_METADATA, "(MIT", "MIT OR", "MIT WITH AND"]:
            self.assertIsNone(notices.parse_expression(text), text)


class NoticesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.folder = tempfile.TemporaryDirectory()
        self.tree = Tree(Path(self.folder.name))

    def tearDown(self) -> None:
        self.folder.cleanup()

    def test_a_package_added_without_writing_the_list_fails_the_check_until_it_is_written(self) -> None:
        self.tree.npm("left-pad", "1.3.0", "MIT")
        self.assertEqual(0, self.tree.run("write")[0])
        self.assertEqual(0, self.tree.run("check")[0])
        self.tree.npm("right-pad", "1.0.1", "MIT")

        code, errors = self.tree.run("check")

        self.assertEqual(1, code)
        self.assertIn("lists other packages than the lock files and the restore resolve", errors)
        self.assertIn("+| right-pad | MIT |", errors)
        self.assertEqual(0, self.tree.run("write")[0])
        self.assertEqual(0, self.tree.run("check")[0])
        self.assertIn("| right-pad | MIT |", self.tree.notices())

    def test_a_new_version_under_the_same_licence_leaves_the_list_as_it_is(self) -> None:
        self.tree.npm("left-pad", "1.3.0", "MIT")
        self.assertEqual(0, self.tree.run("write")[0])
        self.tree.npm("left-pad", "1.3.1", "MIT")

        self.assertEqual(0, self.tree.run("check")[0])

    def test_a_new_version_under_another_licence_fails_the_check(self) -> None:
        self.tree.npm("left-pad", "1.3.0", "MIT")
        self.assertEqual(0, self.tree.run("write")[0])
        self.tree.npm("left-pad", "2.0.0", "Apache-2.0")

        code, errors = self.tree.run("check")

        self.assertEqual(1, code)
        self.assertIn("+| left-pad | Apache-2.0 |", errors)

    def test_a_licence_outside_the_allowed_list_fails_until_the_package_goes_or_the_licence_is_allowed(self) -> None:
        self.tree.npm("left-pad", "1.3.0", "MIT")
        self.tree.npm("copyleft", "2.0.0", "GPL-3.0-only")

        code, errors = self.tree.run("write")

        self.assertEqual(1, code)
        self.assertIn("npm:copyleft 2.0.0: its licence GPL-3.0-only is not on the allowed list", errors)
        del self.tree.lock["node_modules/copyleft"]
        self.tree.save()
        self.assertEqual(0, self.tree.run("write")[0])
        self.tree.npm("copyleft", "2.0.0", "GPL-3.0-only")
        self.tree.set_policy(allowed=["MIT", "Apache-2.0", "GPL-3.0-only"])
        self.assertEqual(0, self.tree.run("write")[0])

    def test_an_exception_admits_its_packages_at_its_version_only_and_puts_its_terms_in_the_notices(self) -> None:
        self.tree.npm("sdk", "0.3.1", "SEE LICENSE IN README.md")
        self.tree.npm("sdk-linux", "0.3.1", "SEE LICENSE IN LICENSE.md")
        self.tree.write("scripts/notices/terms/vendor-sdk.txt", "Vendor terms, in full.\n")
        exception = {"packages": ["npm:sdk", "npm:sdk-linux"], "version": "0.3.1", "licence": "LicenseRef-Vendor-SDK", "terms": "terms/vendor-sdk.txt", "reason": "no alternative"}
        self.tree.set_policy(exceptions=[exception])

        self.assertEqual(0, self.tree.run("write")[0])
        self.assertIn("| sdk-linux | LicenseRef-Vendor-SDK |", self.tree.notices())
        self.assertIn("#### sdk 0.3.1, sdk-linux 0.3.1\n\n```text\nVendor terms, in full.\n```", self.tree.notices())

        del self.tree.lock["node_modules/sdk"]
        self.tree.npm("sdk", "0.4.0", "SEE LICENSE IN README.md")
        code, errors = self.tree.run("write")
        self.assertEqual(1, code)
        self.assertIn("npm:sdk 0.4.0: its metadata says 'SEE LICENSE IN README.md', which is not a licence expression", errors)
        self.assertIn("the exception for npm:sdk 0.3.1 matches no package; remove it", errors)

    def test_a_licence_file_needs_a_declared_entry_and_needs_it_again_when_the_file_changes(self) -> None:
        self.tree.nuget("Bogus", "35.6.5", '<license type="file">LICENSE</license>', "MIT License, 2015.\n")
        self.tree.set_policy(sources=[{"type": "nuget", "path": "artifacts/obj/*/project.assets.json"}])

        code, errors = self.tree.run("write")
        self.assertEqual(1, code)
        metadata = errors.split("its metadata says '")[1].split("'")[0]
        self.assertRegex(metadata, r"^file LICENSE sha256:[0-9a-f]{16}$")

        self.tree.set_policy(declared=[{"package": "nuget:Bogus", "metadata": metadata, "licence": "MIT"}])
        self.assertEqual(0, self.tree.run("write")[0])
        self.assertIn("| Bogus | MIT |", self.tree.notices())

        (self.tree.root / "packages/bogus/35.6.5/LICENSE").write_text("New terms.\n")
        code, errors = self.tree.run("write")
        self.assertEqual(1, code)
        self.assertIn("nuget:Bogus 35.6.5: its metadata says 'file LICENSE sha256:", errors)
        self.assertIn("the declared entry for nuget:Bogus", errors)

    def test_nuget_reads_each_package_of_the_platform_neutral_graph_once(self) -> None:
        self.tree.nuget("Serilog", "4.0.0", '<license type="expression">Apache-2.0</license>')
        self.tree.nuget("Fare", "2.1.1", "<licenseUrl>https://github.com/moodmosaic/Fare/blob/master/LICENCE</licenseUrl>")
        source = {"type": "nuget", "path": "artifacts/obj/*/project.assets.json"}

        licences, problems = notices.judge(notices.nuget_packages(self.tree.root, source, notices.fetch_json), self.tree.policy)

        self.assertEqual({("Serilog", "Apache-2.0"), ("Fare", None)}, {(package.name, licence) for package, licence in licences.items()})
        self.assertEqual(["nuget:Fare 2.1.1: its metadata says 'url https://github.com/moodmosaic/Fare/blob/master/LICENCE'"], [problem.split(", which")[0] for problem in problems])

    def test_a_licence_file_named_with_backslashes_is_read_on_every_system(self) -> None:
        self.tree.nuget("Vendor", "1.0.0", '<license type="file">legal\\LICENSE.txt</license>', "Vendor terms.\n", "legal/LICENSE.txt")
        source = {"type": "nuget", "path": "artifacts/obj/*/project.assets.json"}

        packages = notices.nuget_packages(self.tree.root, source, notices.fetch_json)

        self.assertRegex(packages[0].metadata, r"^file legal/LICENSE.txt sha256:[0-9a-f]{16}$")

    def test_each_lock_file_gets_a_table_of_its_own_packages(self) -> None:
        self.tree.npm("left-pad", "1.3.0", "MIT")
        self.tree.write("tools/package-lock.json", json.dumps({"lockfileVersion": 3, "packages": {"node_modules/esbuild": {"version": "0.28.2", "license": "MIT"}}}))
        self.tree.set_policy(sources=[{"type": "npm", "path": "package-lock.json"}, {"type": "npm", "path": "tools/package-lock.json"}])

        self.assertEqual(0, self.tree.run("write")[0])

        root_table, tools_table = self.tree.notices().split("### npm packages")[1:]
        self.assertIn("left-pad", root_table)
        self.assertNotIn("esbuild", root_table)
        self.assertIn("esbuild", tools_table)
        self.assertNotIn("left-pad", tools_table)

    def test_bun_and_uv_read_each_registry_package_from_its_registry(self) -> None:
        self.tree.write("bun.lock", '{\n  "packages": {\n    "zod": ["zod@4.6.7", "", {}, "sha512-x"],\n    "@scope/sdk": ["@scope/sdk@0.3.1", "", {}, "sha512-y"],\n    "app": ["app@workspace:."],\n  },\n}\n')
        self.tree.write("uv.lock", 'version = 1\n\n[[package]]\nname = "toolbelt"\nversion = "0.1.0"\nsource = { virtual = "." }\n\n[[package]]\nname = "pandas"\nversion = "2.3.0"\nsource = { registry = "https://pypi.org/simple" }\n')
        registry = {
            f"{notices.NPM_REGISTRY}/zod/4.6.7": {"license": "MIT"},
            f"{notices.NPM_REGISTRY}/@scope/sdk/0.3.1": {"license": "SEE LICENSE IN README.md"},
            f"{notices.PYPI}/pandas/2.3.0/json": {"info": {"license": "BSD 3-Clause License", "classifiers": ["License :: OSI Approved :: BSD License", "Programming Language :: Python"]}},
        }

        packages = notices.bun_packages(self.tree.root, {"path": "bun.lock"}, registry.__getitem__) + notices.uv_packages(self.tree.root, {"path": "uv.lock"}, registry.__getitem__)

        self.assertEqual(
            [
                ("bun", "@scope/sdk", "0.3.1", "SEE LICENSE IN README.md"),
                ("bun", "zod", "4.6.7", "MIT"),
                ("uv", "pandas", "2.3.0", "license: BSD 3-Clause License; classifiers: License :: OSI Approved :: BSD License"),
            ],
            [(package.source, package.name, package.version, package.metadata) for package in packages],
        )

    def test_a_package_from_git_or_a_url_fails_instead_of_leaving_the_list(self) -> None:
        self.tree.write("bun.lock", '{\n  "packages": {\n    "tool": ["tool@github:owner/tool#abc123", {}],\n  },\n}\n')
        self.tree.write("uv.lock", 'version = 1\n\n[[package]]\nname = "tool"\nversion = "0.1.0"\nsource = { git = "https://github.com/owner/tool" }\n')

        with self.assertRaisesRegex(notices.NoticesError, "tool github:owner/tool#abc123 comes from outside the registry"):
            notices.bun_packages(self.tree.root, {"path": "bun.lock"}, {}.__getitem__)
        with self.assertRaisesRegex(notices.NoticesError, "tool 0.1.0 comes from outside the registry"):
            notices.uv_packages(self.tree.root, {"path": "uv.lock"}, {}.__getitem__)

    def test_a_dropped_connection_is_tried_again(self) -> None:
        answers = [http.client.RemoteDisconnected("closed"), io.BytesIO(b'{"license": "MIT"}')]

        def urlopen(_request: object, timeout: int) -> io.BytesIO:
            answer = answers.pop(0)
            if isinstance(answer, Exception):
                raise answer
            return answer

        with mock.patch.object(notices.urllib.request, "urlopen", urlopen), mock.patch.object(notices.time, "sleep"):
            self.assertEqual({"license": "MIT"}, notices.fetch_json("https://registry.example/zod/4.6.7"))

    def test_a_notices_file_without_its_markers_fails(self) -> None:
        self.tree.write("THIRD-PARTY-NOTICES.md", "# Third-party notices\n")

        code, errors = self.tree.run("check")

        self.assertEqual(2, code)
        self.assertIn(f"must hold the line {notices.START} once", errors)


if __name__ == "__main__":
    unittest.main()
