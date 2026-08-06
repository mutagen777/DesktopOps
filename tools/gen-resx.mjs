import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const py = fs.readFileSync(path.join(root, "tools/gen-resx.py"), "utf8");

function parseList(name) {
  const start = py.indexOf(`${name} = [`);
  if (start < 0) throw new Error(`list ${name} not found`);
  const open = py.indexOf("[", start);
  let depth = 0;
  let end = -1;
  for (let i = open; i < py.length; i++) {
    const c = py[i];
    if (c === "[") depth++;
    else if (c === "]") {
      depth--;
      if (depth === 0) {
        end = i;
        break;
      }
    }
  }
  if (end < 0) throw new Error(`list ${name} unclosed`);
  const body = py.slice(open + 1, end);
  const entries = [];
  const re = /\("((?:\\.|[^"\\])*)","((?:\\.|[^"\\])*)"\)/g;
  let match;
  while ((match = re.exec(body))) {
    const key = match[1];
    const value = match[2]
      .replace(/\\n/g, "\n")
      .replace(/\\"/g, '"')
      .replace(/\\\\/g, "\\");
    entries.push([key, value]);
  }
  return entries;
}

function resx(entries) {
  const lines = [
    '<?xml version="1.0" encoding="utf-8"?>',
    "<root>",
    '  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>',
    '  <resheader name="version"><value>2.0</value></resheader>',
    '  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>',
    '  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>',
  ];
  for (const [k, v] of entries) {
    const escaped = v.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    lines.push(`  <data name="${k}" xml:space="preserve"><value>${escaped}</value></data>`);
  }
  lines.push("</root>");
  return lines.join("\n") + "\n";
}

const lists = ["admin_en", "admin_de", "agent_en", "agent_de"];
const parsed = Object.fromEntries(lists.map((n) => [n, parseList(n)]));

fs.mkdirSync(path.join(root, "src/DesktopOps.Admin/Resources"), { recursive: true });
fs.mkdirSync(path.join(root, "src/DesktopOps.Agent/Resources"), { recursive: true });
fs.writeFileSync(path.join(root, "src/DesktopOps.Admin/Resources/Ui.resx"), resx(parsed.admin_en));
fs.writeFileSync(path.join(root, "src/DesktopOps.Admin/Resources/Ui.de.resx"), resx(parsed.admin_de));
fs.writeFileSync(path.join(root, "src/DesktopOps.Agent/Resources/Strings.resx"), resx(parsed.agent_en));
fs.writeFileSync(path.join(root, "src/DesktopOps.Agent/Resources/Strings.de.resx"), resx(parsed.agent_de));
console.log("ok", parsed.admin_en.length, parsed.agent_en.length);
