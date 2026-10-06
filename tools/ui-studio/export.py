"""Exports a UI Studio panel (an .html file) to a .layout.json without opening the studio, using the studio's own
exporter in headless Edge or Chrome. Usage: python export.py panel.html out.layout.json"""
import json, os, re, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
BROWSERS = [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            r"C:\Program Files\Google\Chrome\Application\chrome.exe"]


def main(src, out):
    studio = open(os.path.join(HERE, "index.html"), encoding="utf-8").read()
    panel = open(src, encoding="utf-8").read()
    # The studio, with the panel as its code and a hook that writes the export into the page for --dump-dom.
    hook = ("<script>code.value = %s; render(); settled().then(() => { const r = buildLayout();"
            " const pre = document.createElement('pre'); pre.id = 'out';"
            " pre.textContent = JSON.stringify(r.error ? { error: r.error } : { layout: r.layout, warnings: r.warnings });"
            " document.body.appendChild(pre); });</script></body>") % json.dumps(panel)
    page = studio.replace("try { code.value = localStorage", "try { if (false) code.value = localStorage")
    page = page[:page.rindex("</body>")] + hook + page[page.rindex("</body>") + 7:]
    fd, tmp = tempfile.mkstemp(suffix=".html")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(page)
    try:
        browser = next(b for b in BROWSERS if os.path.exists(b))
        dom = subprocess.run([browser, "--headless=new", "--disable-gpu", "--window-size=1920,1080",
                              "--virtual-time-budget=8000", "--dump-dom", "file:///" + tmp.replace("\\", "/")],
                             capture_output=True, text=True, encoding="utf-8", timeout=60).stdout
    finally:
        os.remove(tmp)
    m = re.search(r'<pre id="out">(.*?)</pre>', dom, re.S)
    if not m:
        sys.exit("no export from the browser")
    result = json.loads(m.group(1).replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", '"').replace("&amp;", "&"))
    if "error" in result:
        sys.exit(result["error"])
    for w in result["warnings"]:
        print("warning:", w)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(result["layout"], f, indent=1)
    n = json.dumps(result["layout"]).count('"type"')
    print(f"exported {n} elements to {out}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
