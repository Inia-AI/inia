"""Checks an edited workflow file against the workflow JSON Schema and prints every problem.

Usage: python3 check_workflow.py <workflow.json> <workflow.schema.json>
Exit code 0 when the workflow matches, 1 when it does not, 2 when a file cannot be read.

Supports exactly the keywords WorkflowJsonSchema emits; WorkflowJsonSchemaTests pins that set.
"""

import json
import sys

MAX_REPORTED = 50

TYPE_CHECKS = {
    "object": lambda value: isinstance(value, dict),
    "array": lambda value: isinstance(value, list),
    "string": lambda value: isinstance(value, str),
    "integer": lambda value: isinstance(value, int) and not isinstance(value, bool),
    "number": lambda value: isinstance(value, (int, float)) and not isinstance(value, bool),
    "boolean": lambda value: isinstance(value, bool),
    "null": lambda value: value is None,
}


def resolve(root, schema):
    while "$ref" in schema:
        schema = root["definitions"][schema["$ref"].split("/")[-1]]
    return schema


def check(root, schema, value, path, errors):
    schema = resolve(root, schema)
    start = len(errors)

    if "type" in schema:
        allowed = schema["type"] if isinstance(schema["type"], list) else [schema["type"]]
        if not any(TYPE_CHECKS[name](value) for name in allowed):
            errors.append(f"{path}: expected {' or '.join(allowed)}, found {type_name(value)}")
            return

    if "enum" in schema and value not in schema["enum"]:
        errors.append(f"{path}: {json.dumps(value)} is not one of {json.dumps(schema['enum'])}")
    if "const" in schema and value != schema["const"]:
        errors.append(f"{path}: must be {json.dumps(schema['const'])}")

    if isinstance(value, dict):
        for name in schema.get("required", []):
            if name not in value:
                errors.append(f"{path}: missing required key '{name}'")
        properties = schema.get("properties", {})
        for name, item in value.items():
            if name in properties:
                check(root, properties[name], item, f"{path}.{name}", errors)
            elif schema.get("additionalProperties") is False:
                errors.append(f"{path}: unknown key '{name}' (known keys: {', '.join(sorted(properties))})")
            elif isinstance(schema.get("additionalProperties"), dict):
                check(root, schema["additionalProperties"], item, f"{path}.{name}", errors)

    if isinstance(value, list) and "items" in schema:
        for index, item in enumerate(value):
            check(root, schema["items"], item, f"{path}[{index}]", errors)

    for branch in schema.get("allOf", []):
        condition = branch.get("if")
        if condition is None:
            check(root, branch, value, path, errors)
        elif matches(root, condition, value):
            check(root, branch["then"], value, path, errors)

    if "anyOf" in schema and not any(matches(root, branch, value) for branch in schema["anyOf"]):
        attempts = [explain(root, branch, value, path) for branch in schema["anyOf"]]
        errors.append(f"{path}: matches none of the allowed shapes: " + " | ".join(attempts))

    return len(errors) == start


def matches(root, schema, value):
    return check(root, schema, value, "", [])


def explain(root, schema, value, path):
    errors = []
    check(root, schema, value, path, errors)
    return errors[0] if errors else "ok"


def type_name(value):
    return next((name for name, test in TYPE_CHECKS.items() if name != "number" and test(value)), "number")


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    try:
        with open(sys.argv[1], encoding="utf-8-sig") as workflow_file:
            workflow = json.load(workflow_file)
        with open(sys.argv[2], encoding="utf-8") as schema_file:
            schema = json.load(schema_file)
    except (OSError, ValueError) as error:
        print(f"cannot read input: {error}")
        return 2

    errors = []
    check(schema, schema, workflow, "workflow", errors)
    if not errors:
        print("OK: the workflow matches the schema")
        return 0

    for error in errors[:MAX_REPORTED]:
        print(error)
    if len(errors) > MAX_REPORTED:
        print(f"... and {len(errors) - MAX_REPORTED} more")
    return 1


if __name__ == "__main__":
    sys.exit(main())
