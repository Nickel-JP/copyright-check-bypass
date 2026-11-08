import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ID = "com.nickel-jp.copyright-check-bypass"
GITHUB_REPO = "Nickel-JP/copyright-check-bypass"
LISTING_URL = "https://nickel-jp.github.io/copyright-check-bypass/index.json"
ROOT_FILES = ("package.json", "README.md", "CHANGELOG.md", "LICENSE", "Editor.meta")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check_package(manifest):
    if manifest["name"] != PACKAGE_ID or not re.fullmatch(r"\d+\.\d+\.\d+", manifest["version"]):
        raise ValueError("パッケージIDまたはバージョンが不正です。")
    if manifest.get("dependencies") or manifest.get("vpmDependencies"):
        raise ValueError("SDKへの依存を追加しない方針と一致しません。")
    if manifest.get("license") != "MIT":
        raise ValueError("公開ライセンスはMITです。")
    assembly = read_json(ROOT / "Editor/Nogut.CopyrightCheckBypass.Editor.asmdef")
    if assembly["includePlatforms"] != ["Editor"] or assembly["references"] or assembly["precompiledReferences"]:
        raise ValueError("Editor専用アセンブリの参照設定が不正です。")
    if not assembly["overrideReferences"] or assembly["noEngineReferences"]:
        raise ValueError("Unity以外の自動参照を無効にする必要があります。")


def package_files():
    required = set()
    for name in ROOT_FILES:
        required.add(name)
        if name != "Editor.meta":
            required.add(name + ".meta")
    editor_files = {
        "Editor/Copyright Check Bypass.cs",
        "Editor/Copyright Check Bypass.cs.meta",
        "Editor/Nogut.CopyrightCheckBypass.Editor.asmdef",
        "Editor/Nogut.CopyrightCheckBypass.Editor.asmdef.meta",
    }
    required.update(editor_files)
    missing = sorted(name for name in required if not (ROOT / name).is_file())
    actual_editor = {path.relative_to(ROOT).as_posix()
                     for path in (ROOT / "Editor").rglob("*") if path.is_file()}
    unexpected = sorted(actual_editor - editor_files)
    if missing or unexpected:
        raise ValueError(f"配布対象ファイルが不正です。欠落: {missing}、想定外: {unexpected}")
    paths = [ROOT / name for name in sorted(required)]
    guids = set()
    for path in paths:
        if path.suffix != ".meta":
            continue
        match = re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(encoding="utf-8"), re.MULTILINE)
        if not match or match.group(1) in guids:
            raise ValueError("metaのGUIDが不正または重複しています。")
        guids.add(match.group(1))
    return paths


def main():
    parser = argparse.ArgumentParser(description="VPM配布用ZIPを生成し、配布JSONとの整合性を確認します。")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--write-index", action="store_true")
    mode.add_argument("--check", action="store_true")
    parser.add_argument("--output", type=Path, default=ROOT / "dist")
    args = parser.parse_args()
    manifest = read_json(ROOT / "package.json")
    check_package(manifest)
    version = manifest["version"]
    zip_name = f"{PACKAGE_ID}-{version}.zip"
    download_url = f"https://github.com/{GITHUB_REPO}/releases/download/v{version}/{zip_name}"
    if manifest["url"] != download_url:
        raise ValueError("package.jsonのダウンロードURLがバージョンと一致しません。")
    args.output.mkdir(parents=True, exist_ok=True)
    archive = args.output / zip_name
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for path in sorted(package_files()):
            entry = zipfile.ZipInfo(path.relative_to(ROOT).as_posix(), (2026, 11, 8, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            data = path.read_bytes().replace(b"\r\n", b"\n")
            output.writestr(entry, data, compresslevel=9)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (args.output / "SHA256SUMS.txt").write_text(f"{digest}  {zip_name}\n", encoding="utf-8")
    listing_path = ROOT / "docs/index.json"
    listing = read_json(listing_path) if listing_path.exists() else {
        "name": "Copyright Check Bypass VPM Repository",
        "id": PACKAGE_ID + ".repository",
        "author": "Nickel-JP",
        "url": LISTING_URL,
        "packages": {},
    }
    entry = dict(manifest, zipSHA256=digest)
    if args.write_index:
        listing["packages"].setdefault(PACKAGE_ID, {"versions": {}})["versions"][version] = entry
        listing_path.write_text(json.dumps(listing, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    else:
        if listing["url"] != LISTING_URL or listing["id"] != PACKAGE_ID + ".repository":
            raise ValueError("リポジトリのURLまたはIDが一致しません。")
        if listing["packages"][PACKAGE_ID]["versions"][version] != entry:
            raise ValueError("公開用JSONとZIPが一致しません。--write-indexで更新してください。")
    with zipfile.ZipFile(archive) as output:
        if output.testzip() is not None or "package.json" not in output.namelist():
            raise ValueError("ZIPの形式が不正です。")
        if read_json(ROOT / "package.json") != json.loads(output.read("package.json")):
            raise ValueError("ZIP内のmanifestが一致しません。")
    print(json.dumps({"archive": str(archive), "sha256": digest, "version": version,
                      "files": len(package_files()), "listing": LISTING_URL}, ensure_ascii=False))


if __name__ == "__main__":
    main()
