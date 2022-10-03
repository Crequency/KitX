
import os
import chardet

ignore_ext_name = ['.git', 'pack', 'idx', 'dockerignore', 'gitignore', 'png', 'targets', 'cache', 'axaml', 'ttf', 'dll', 'pdb', 'ico', 'xsd', 'resx', 'rc', 'razor', 'eot', 'otf', 'svg', 'woff', 'woff2', 'zip', 'so', 'dylib', 'nuspec', 'nupkg']

def correctCoding(dir):
    for i in os.listdir(dir):
        path = os.path.join(dir,i)
        if i[0] == ".":
            continue
        if i.split(".")[-1] in ["ttf", "dll", "png", "jpg", "so", "zip", "ico", "dylib", "nupkg", "pdb"]:
            continue
        if os.path.isdir(path):
            correctCoding(path)
            continue
        if not '.' in i:
            continue
        print("fixing", path)
        try:
            fix_content = ""
            charset = ""
            with open(path, "rb") as fp:
                content = fp.read()
                charset = chardet.detect(content)
                fix_content = content.decode(encoding=charset["encoding"])
            with open(path, "wb") as fp:
                fp.write(fix_content.encode(encoding="utf-8_sig"))
            print("fixed", path, "from", charset["encoding"], "to utf-8_sig")
        except Exception as e:
            print(e)

if __name__ == "__main__":
    correctCoding(os.getcwd())
