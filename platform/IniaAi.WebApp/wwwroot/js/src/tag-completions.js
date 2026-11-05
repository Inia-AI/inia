// The completion source must speak the grammar of its host: agent-block prompts render under
// the strict double-brace agent-template grammar (AgentProperties.cs), every other block under
// the engine's single-brace grammar (GET_PLACEHOLDERS in IniaAi.Common/Extensions/
// StringExtensions.cs). Inserting the wrong arity looks right in the editor, saves cleanly,
// and hands the run literal text instead of the value — so the grammar is chosen per editor
// instance, defaulting to single-brace to leave every non-agent block untouched.
const TAG_NAME_CHARS = '[a-zA-Z0-9._\\-\\[\\] &\\/()%]';

const GRAMMARS = {
    single: {
        partialPattern: new RegExp(`\\{${TAG_NAME_CHARS}*$`),
        validFor: new RegExp(`^\\{${TAG_NAME_CHARS}*\\}?$`),
        label: (name) => `{${name}}`,
    },
    // The partial pattern swallows both typed braces so applying a completion after "{{" never
    // stacks a third brace in front of the inserted "{{name}}".
    double: {
        partialPattern: new RegExp(`\\{\\{?${TAG_NAME_CHARS}*$`),
        validFor: new RegExp(`^\\{\\{?${TAG_NAME_CHARS}*\\}?\\}?$`),
        label: (name) => `{{${name}}}`,
    },
};

export function grammarFor(tagGrammar) {
    return GRAMMARS[tagGrammar] || GRAMMARS.single;
}
