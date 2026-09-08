"""Download fixed build dependencies. Python 3.12+, .NET SDK 9.0.310 required."""
import hashlib
import pathlib
import shutil
import tarfile
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
TOOLS = ROOT / '.tools'
TOOLS.mkdir(exist_ok=True)
EXPECTED = {'godot.zip':'2b137e69c9190ce653a5ca3a7fee1f33dbe1febd7b63ac6babd52968c6ddcec3',
            'templates.tpz':'b64d164b96b19dd7175a1ba3f3a6e583e9d4dc507b3d1333563f246b5811823e',
            'pdfium.tgz':'55329d5cb5de8a379a2fc563106492d7f385a1f795d18970922c71f708f9fbb4'}

def download(url, filename):
    path = TOOLS / filename
    if not path.exists():
        print('Downloading', filename, flush=True)
        urllib.request.urlretrieve(url, path)
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if digest != EXPECTED[filename]:
        raise RuntimeError('Checksum mismatch: ' + filename)
    return path

base = 'https://github.com/godotengine/godot/releases/download/4.4.1-stable/'
editor = download(base + 'Godot_v4.4.1-stable_mono_win64.zip', 'godot.zip')
with zipfile.ZipFile(editor) as archive:
    archive.extractall(TOOLS)
templates = download(base + 'Godot_v4.4.1-stable_mono_export_templates.tpz', 'templates.tpz')
with zipfile.ZipFile(templates) as archive:
    for name in archive.namelist():
        if 'windows' in name:
            archive.extract(name, TOOLS)
pdf = download('https://github.com/bblanchon/pdfium-binaries/releases/download/chromium/7999/pdfium-win-x64.tgz', 'pdfium.tgz')
with tarfile.open(pdf) as archive:
    archive.extractall(TOOLS / 'pdfium', filter='data')
native = ROOT / 'src/App/native'
native.mkdir(parents=True, exist_ok=True)
shutil.copy2(TOOLS / 'pdfium/bin/pdfium.dll', native / 'pdfium.dll')
licenses = ROOT / 'licenses'
licenses.mkdir(exist_ok=True)
shutil.copy2(TOOLS / 'pdfium/LICENSE', licenses / 'PDFium.txt')
shutil.copytree(TOOLS / 'pdfium/licenses', licenses / 'PDFium-third-party', dirs_exist_ok=True)
# Godot export publishes a self-contained .NET runtime. Cache the exact SDK-selected packs.
feed = TOOLS / 'Godot_v4.4.1-stable_mono_win64/GodotSharp/Tools/nupkgs'
for name in ['microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64', 'microsoft.aspnetcore.app.runtime.win-x64']:
    filename = name + '.8.0.23.nupkg'
    if not (feed / filename).exists():
        urllib.request.urlretrieve('https://api.nuget.org/v3-flatcontainer/' + name + '/8.0.23/' + filename, feed / filename)
print('Dependencies ready. SHA256:')
for path in [editor, templates, pdf]:
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if digest != EXPECTED[path.name]:
        raise RuntimeError('Checksum mismatch: ' + path.name)
    print(path.name, digest)
