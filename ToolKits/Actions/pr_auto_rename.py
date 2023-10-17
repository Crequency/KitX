# Update the name of PR

import sys
import re

left_brackets_cn = ["『", "「", "〔", "【", "（", "《", "〈"]
right_brackets_cn = ["』", "」", "〕", "】", "）", "》", "〉"]

left_quotation_cn = ["“", "‘"]
right_quotation_cn = ["”", "’"]

left_brackets_en_replace_to = ["[", "[", "{", "[", "(", "<<", "<"]
right_brackets_en_replace_to = ["]", "]", "}", "]", ")", ">>", ">"]

left_quotation_en = ['"', "'"]
right_quotation_en = ['"', "'"]

connection_signs = ["_", "-"]

def convert_cn_to_en(title: str):
    for i in range(len(left_brackets_cn)):
        title = title.replace(left_brackets_cn[i], left_brackets_en_replace_to[i]).replace(
            right_brackets_cn[i], right_brackets_en_replace_to[i]
        )

    for i in range(len(left_quotation_cn)):
        title = title.replace(left_quotation_cn[i], left_quotation_en[i]).replace(
            right_quotation_cn[i], left_quotation_en[i]
        )

    return title


def check_and_format(title: str):
    """
    Check and update the name of PR.

    Args:
        title (str): _description_

    Returns:
        _type_: _description_
    """

    title = convert_cn_to_en(title.strip())

    regex_check = (
        r"^(\[?[pP][rR]\]?|\[?[pP][uU][lL][lL][- _]?[rR][eE][qQ][uU][eE][sS][tT]\]?|\[?[pP][uU][lL][lL]\]?) ([^\n]*)$"
    )

    if not title.startswith("[Pull Request]"):
        result = re.match(regex_check, title, re.M | re.I)

        if result:
            title = "[Pull Request] " + result.group(2)
        else:
            title = "[Pull Request] " + title

    return title

print(check_and_format(sys.argv[1]))
