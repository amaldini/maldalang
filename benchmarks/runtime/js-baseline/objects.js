// Hand-written JavaScript twin of objects.malda (baseline for the js backend).
class Vec {
  constructor(x, y) {
    this.x = x;
    this.y = y;
  }

  dotWith(other) {
    return this.x * other.x + this.y * other.y;
  }
}

let acc = 0;
const a = new Vec(1, 2);
for (let i = 0; i < 300000; i++) {
  const b = new Vec(i % 7, i % 5);
  acc = acc + a.dotWith(b);
}
console.log(acc);
