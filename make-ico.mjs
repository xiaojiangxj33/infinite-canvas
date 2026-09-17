// 把 256×256 的 PNG 包成 .ico（Vista+ 支持 PNG 内嵌的 ICO）。
// 不引第三方库：ICO 头 6 字节 + 目录项 16 字节 + PNG 原始字节。
import { readFileSync, writeFileSync } from "node:fs";

const src = process.argv[2];
const out = process.argv[3];
if (!src || !out) {
    console.error("用法: node make-ico.mjs <输入.png> <输出.ico>");
    process.exit(1);
}

const png = readFileSync(src);
if (png.length < 8 || png.readUInt32BE(0) !== 0x89504e47) {
    console.error("输入不是 PNG");
    process.exit(1);
}

const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0); // reserved
header.writeUInt16LE(1, 2); // type: 1 = icon
header.writeUInt16LE(1, 4); // 图像数量

const entry = Buffer.alloc(16);
entry.writeUInt8(0, 0); // 宽 0 表示 256
entry.writeUInt8(0, 1); // 高 0 表示 256
entry.writeUInt8(0, 2); // 调色板数
entry.writeUInt8(0, 3); // reserved
entry.writeUInt16LE(1, 4); // color planes
entry.writeUInt16LE(32, 6); // bits per pixel
entry.writeUInt32LE(png.length, 8); // 数据长度
entry.writeUInt32LE(22, 12); // 数据偏移 = 6 + 16

writeFileSync(out, Buffer.concat([header, entry, png]));
console.log(`已生成 ${out}（${(png.length / 1024).toFixed(1)} KB PNG 内嵌）`);
