"""
Format the pull request title to the standard format.
"""

import sys
import re

# Chinese replacement.
replacemap = {
    # Brackets.
    "（": "(",
    "）": ")",
    "『": "[",
    "』": "]",
    "「": "[",
    "」": "]",
    "【": "[",
    "】": "]",
    "〔": "{",
    "〕": "}",
    # Quotation marks.
    "‘": "'",
    "’": "'",
    "“": '"',
    "”": '"',
    "，": ", ",
    "。": ". ",
    "；": "; ",
    "：": ": ",
    "？": "? ",
    "！": "! ",
    "、": ", ",
    "…": "...",
    "—": "-",
    "·": ".",
    "～": "~",
}


HEAD = "[Pull Request]"
FORMAT_REGEX = r"^(\[?[pP][rR]\]?|\[?[pP][uU][lL][lL][- _]?[rR][eE][qQ][uU][eE][sS][tT]\]?|\[?[pP][uU][lL][lL]\]?)([^\n]*)$"  # pylint: disable=line-too-long

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(f"Usage: python {sys.argv[0]}  <title>")
        sys.exit(1)
    title = sys.argv[1]

    # Step 1: Convert Chinese to English.
    for key, value in replacemap.items():
        title = title.replace(key, value)

    # Step 2: Check and update the name of PR.
    if not title.startswith(HEAD):
        result = re.match(FORMAT_REGEX, title, re.M | re.I)
        if result:
            title = f"{HEAD} {result.group(2).strip()}"
        else:
            title = f"{HEAD} {title.strip()}"

    print(title)
