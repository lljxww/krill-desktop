"""Validate the version format shared by tags, app bundles, and .NET assemblies."""
import argparse
import re


def normalize(value: str, *, tag: bool = False) -> str:
    pattern = r"v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)" if tag else r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    match = re.fullmatch(pattern, value)
    if not match or any(int(part) > 65535 for part in match.groups()):
        raise ValueError("Expected vX.Y.Z tag (or X.Y.Z version), with each component between 0 and 65535.")
    return ".".join(match.groups())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("value")
    parser.add_argument("--tag", action="store_true")
    args = parser.parse_args()
    try:
        print(normalize(args.value, tag=args.tag))
    except ValueError as error:
        parser.error(str(error))
