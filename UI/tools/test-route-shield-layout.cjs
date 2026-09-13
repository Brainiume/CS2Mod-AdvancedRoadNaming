const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname, "../src/components/routeShields/routeShieldLayout.ts"), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } });
const context = { exports: {} };
vm.runInNewContext(compiled.outputText, context);
const { selectNonOverlappingShields: layout, rectangleWidth } = context.exports;
const shield = (id, left, top = 0, extra = {}) => ({ id, left, top, style: "AustralianARectangle", label: "A1", scale: 1, occluded: false, ...extra });
const ids = (items) => Array.from(items, x => x.id);

assert.deepEqual(ids(layout([shield(1, 0), shield(2, 0)], [], 1)), [1], "Duplicate saved/edit shields");
assert.deepEqual(ids(layout([shield(1, 0), shield(2, 60)], [], 1)), [1, 2], "Separated shields stay on their roads");
assert.deepEqual(ids(layout([shield(1, 0), shield(2, 60)], [], 2)), [1], "Interface scale changes collision size");
assert.deepEqual(ids(layout([shield(1, 0, 0, { scale: 2 }), shield(2, 65)], [], 1)), [1], "Shield size changes collision size");
assert.deepEqual(ids(layout([shield(1, 0, 0, { occluded: true }), shield(2, 0)], [], 1)), [2], "Occluded candidates do not hide visible ones");
assert.deepEqual(ids(layout([shield(1, 95), shield(2, 97)], [], 1)), [1], "Collisions across grid cells");
assert.deepEqual(ids(layout([shield(1, -95), shield(2, -97)], [], 1)), [1], "Negative screen coordinates");
assert.deepEqual(ids(layout([shield(1, 0), shield(2, 0, 50)], [], 1)), [1, 2], "Vertical separation");
assert.equal(rectangleWidth("M12345"), 84, "Long labels use the rendered rectangle width");
const catalog = [{ id: "Imported:wide", widthRem: 180, heightRem: 40 }];
assert.deepEqual(ids(layout([shield(1, 0, 0, { style: "Imported:wide" }), shield(2, 95)], catalog, 1)), [1], "Imported artwork dimensions");
const candidates = Array.from({ length: 256 }, (_, i) => shield(i, (i % 16) * 20, Math.floor(i / 16) * 20));
const accepted = layout(candidates, [], 1);
assert.ok(accepted.length > 1 && accepted.length < candidates.length);
for (let i = 0; i < accepted.length; i++) {
    assert.equal(accepted[i], candidates[accepted[i].id], "Layout preserves positions and identity");
    for (let j = i + 1; j < accepted.length; j++) {
        assert.ok(Math.abs(accepted[i].left - accepted[j].left) >= 54 || Math.abs(accepted[i].top - accepted[j].top) >= 42,
            "No accepted artwork bounds overlap");
    }
}
console.log(`PASS: route-shield layout cases; ${accepted.length} non-overlapping shields selected from 256 candidates.`);
