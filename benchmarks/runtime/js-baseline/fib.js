// Hand-written JavaScript twin of fib.malda (baseline for the js backend).
function fib(n) {
  if (n < 2) {
    return n;
  }
  return fib(n - 1) + fib(n - 2);
}

console.log(fib(27));
