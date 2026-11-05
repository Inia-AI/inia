import pathlib
import tempfile
import unittest

import check_docs


class CheckDocsTests(unittest.TestCase):
    def setUp(self):
        self._folder = tempfile.TemporaryDirectory()
        self.tree = pathlib.Path(self._folder.name)
        (self.tree / "docs").mkdir()
        (self.tree / "IniaAi.Core" / "Workflows").mkdir(parents=True)
        (self.tree / "IniaAi.Core" / "Workflows" / "WorkflowHandler.cs").write_text("", encoding="utf-8")
        (self.tree / "docs" / "guide.md").write_text("# Guide\n\n## Run a `workflow`, step by step\n", encoding="utf-8")

    def tearDown(self):
        self._folder.cleanup()

    def page(self, text, name="README.md"):
        (self.tree / name).write_text(text, encoding="utf-8")
        return check_docs.problems(self.tree)

    def test_links_to_files_and_headings_that_exist_pass(self):
        self.assertEqual([], self.page("See [the guide](docs/guide.md#run-a-workflow-step-by-step) and [docs](docs/).\n"))

    def test_a_link_to_a_missing_file_fails_and_names_it(self):
        self.assertEqual(["README.md: the link to docs/missing.md reaches no file"], self.page("[x](docs/missing.md)\n"))

    def test_a_link_to_a_missing_heading_fails_and_names_it(self):
        self.assertEqual(
            ["README.md: docs/guide.md has no heading for the anchor #run-it"],
            self.page("[x](docs/guide.md#run-it)\n"))

    def test_an_anchor_on_the_same_page_must_name_one_of_its_headings(self):
        self.assertEqual(
            ["README.md: README.md has no heading for the anchor #later"],
            self.page("# Top\n\n[x](#top) [y](#later)\n"))

    def test_a_link_to_a_host_off_the_list_fails_and_names_the_host(self):
        self.assertEqual(
            ["README.md: the link to https://internal.example.com/page goes to internal.example.com, which is not on ALLOWED_HOSTS"],
            self.page("[x](https://internal.example.com/page) [y](https://github.com/x)\n"))

    def test_a_link_inside_a_code_block_is_not_a_link(self):
        self.assertEqual([], self.page("```bash\necho [x](docs/missing.md)\n```\n"))

    def test_a_named_repository_path_that_does_not_exist_fails_on_a_docs_page(self):
        self.assertEqual(
            ["docs/tour.md: names IniaAi.Core/Workflows/Missing.cs, which is not in the tree"],
            self.page("`IniaAi.Core/Workflows/WorkflowHandler.cs` and `IniaAi.Core/Workflows/Missing.cs`\n", "docs/tour.md"))

    def test_a_project_that_does_not_exist_fails_on_a_docs_page(self):
        self.assertEqual(
            ["docs/tour.md: names IniaAi.Gone/Thing.cs, which is not in the tree"],
            self.page("`IniaAi.Gone/Thing.cs`\n", "docs/tour.md"))

    def test_a_path_that_git_ignores_is_one_the_reader_creates(self):
        (self.tree / ".gitignore").write_text("# local files\n.vscode/launch.json\nappsettings.Tests.json\n", encoding="utf-8")

        self.assertEqual([], self.page("`.vscode/launch.json` and `IniaAi.Core/appsettings.Tests.json`\n", "docs/tour.md"))

    def test_settings_routes_and_container_paths_are_not_repository_paths(self):
        self.assertEqual([], self.page(
            "`PlatformSettings:Cohere:ApiKey`, `api/v1/workflow-executions/submit`, `/run/settings`, `IniaAi.Core.dll`\n",
            "docs/tour.md"))


if __name__ == "__main__":
    unittest.main()
