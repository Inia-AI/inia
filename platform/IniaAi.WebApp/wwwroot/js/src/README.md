# prompt-editor bundle source

`wwwroot/js/prompt-editor.bundle.js` is generated from `prompt-editor.entry.js` (and the modules it imports, e.g. `tag-completions.js`) in this directory — never edit the bundle by hand.

Rebuild after changing any source file, and run the tests:

```bash
cd IniaAi.WebApp/wwwroot/js/src
npm install
npm run build
npm test
```

The bundle is committed so app builds and on-prem deployments need no Node toolchain; Node (with esbuild) is only required to regenerate it. CodeMirror versions are pinned via `package-lock.json`.

The placeholder regex in `prompt-editor.entry.js` mirrors `GET_PLACEHOLDERS` in `IniaAi.Common/Extensions/StringExtensions.cs` (as does `wwwroot/js/tag-highlight.js`) — keep all three in sync if the engine's placeholder charset ever changes.

Autocomplete inserts tags in the grammar of the hosting editor (`tag-completions.js`): the agent block's prompts pass `DoubleBraceTags="true"` through `PromptEditor.razor` and get `{{tag}}` completions for the strict double-brace agent-template grammar; every other block keeps single-brace `{tag}`. `tag-completions.test.js` (`npm test`, plain `node --test`, no extra dependencies) pins both grammars.
