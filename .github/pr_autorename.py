#!/usr/bin/env python3
"""Update the name of PR.
"""
import sys
def check(title:str):
    """Check and update the name of PR.

    Args:
        title (str): _description_

    Returns:
        _type_: _description_
    """

    title = title.strip().replace("【", "[").replace("】", "]")
    if(not title.startswith("[Pull Request]")):
        temp = title.replace("(", "").replace("{", "").replace("[", "") \
            .replace(")", "").replace("}", "").replace("]", "") \
            .replace(" ", "").replace("_", "").replace("-", "") \
            .lower().replace("ull", "").replace("equest", "")
        if(temp.startswith("pr")):
            temp = title.lower().replace("(", "[").replace("{", "[").replace(")", "]").replace("}", "]").replace("-", "_").replace(" ", "_")
            content = ""
            # 1. '[pull_request]: <title>'
            if(temp.startswith("[pull_request]:")):
                content = title[len("[pull_request]:"):].strip()
            # 2. '[pull request] <title>'
            elif(temp.startswith("[pull_request]")):
                content = title[len("[pull_request]"):].strip()
            
            # 3. '[pr]: <title>'
            elif(temp.startswith("[pr]:")):
                content = title[len("[pr]:"):].strip()
            # 4. '[pr] <title>'
            elif(temp.startswith("[pr]")):
                content = title[len("[pr]"):].strip()

            # 5. 'pull request: <title>'
            elif(temp.startswith("pull_request:")):
                content = title[len("pull_request:"):].strip()
            # 5. 'pr: <title>'
            elif(temp.startswith("pr:")):
                content = title[len("pr:"):].strip()
            else:
                content = title
            title = "[Pull Request] " + content
        else:
            title = "[Pull Request] " + title
    return title

print(check(sys.argv[1]))
