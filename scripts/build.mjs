// Builds dist/Nessafy (the Spicetify theme folder) and, on Windows, dist/NessafyInstaller.exe
// with the theme inside it. Usage: node scripts/build.mjs [--theme-only]

import { execFileSync } from "child_process";
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "fs";
import { join } from "path";

const themeDir = join("dist", "Nessafy");
rmSync(themeDir, { recursive: true, force: true });
mkdirSync(themeDir, { recursive: true });

const read = name => readFileSync(join("theme", name), "utf8");
writeFileSync(join(themeDir, "color.ini"), read("color.ini"));
writeFileSync(join(themeDir, "user.css"), read("assets.css") + "\n" + read("theme.css"));
if (existsSync(join("theme", "theme.js"))) writeFileSync(join(themeDir, "theme.js"), read("theme.js"));
console.log(`Built ${themeDir}`);

if (process.argv.includes("--theme-only")) process.exit(0);

if (process.platform !== "win32") {
    console.error("The installer can only be built on Windows.");
    process.exit(1);
}

// The C# compiler that ships with Windows (.NET Framework 4), so nothing has to be downloaded.
const csc = join(process.env.WINDIR ?? "C:\\Windows", "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
if (!existsSync(csc)) {
    console.error(`No C# compiler at ${csc}`);
    process.exit(1);
}

// The release this theme is, as the release title says it: the installer's background check compares them.
let version = process.env.NESSAFY_HASH;
if (!version) {
    try {
        version = execFileSync("git", ["rev-parse", "--short", "HEAD"], { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim();
    } catch {
        version = "dev";
    }
}
writeFileSync(join("dist", "version.txt"), version);

const src = join("installer", "src");
const out = join("dist", "NessafyInstaller.exe");
const resources = {
    "icon.ico": join("installer", "build", "icon.ico"),
    "appicon.png": join("installer", "build", "appicon.png"),
    "version.txt": join("dist", "version.txt"),
    ...Object.fromEntries(readdirSync(themeDir).map(f => ["theme/" + f, join(themeDir, f)]))
};

execFileSync(csc, [
    "-nologo",
    "-target:winexe",
    "-optimize+",
    "-platform:anycpu",
    `-out:${out}`,
    `-win32icon:${resources["icon.ico"]}`,
    `-win32manifest:${join(src, "app.manifest")}`,
    ...["System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", "System.Web.Extensions.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll"].map(r => `-reference:${r}`),
    ...Object.entries(resources).map(([name, path]) => `-resource:${path},${name}`),
    ...readdirSync(src).filter(f => f.endsWith(".cs")).map(f => join(src, f))
], { stdio: "inherit" });

console.log(`Built ${out}`);
