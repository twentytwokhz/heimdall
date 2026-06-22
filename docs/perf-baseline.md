# Performance baseline

Roslyn expression engine, measured on the dev machine (.NET 10, Debug build). These are indicative
order-of-magnitude figures, not a formal benchmark. The point is that the warm edit->test loop stays
well under the 2 s target (TQ3).

| Scenario | Time |
|---|---|
| Cold first compile (Roslyn pipeline JIT + first script) | ~560 ms |
| Warm compile of a new, unique expression (engine already warm) | ~40 ms |
| Cache hit (re-evaluate the same expression text) | ~0.06 ms |

## Takeaways

- The ~560 ms cold cost is paid **once, at host start**, by `ExpressionWarmupHostedService` (off the
  request path), so the first real requests do not see it.
- After warm-up, compiling a new unique expression is ~40 ms; repeated evaluations are effectively
  free via the `(code, return type)` compile cache.
- Comfortably under the 2 s target.

Method: `RoslynExpressionEvaluator`, sequential evaluations of `@(1+1)`, then `@(2+2)`, then `@(1+1)`
again (cold, warm-compile, cache-hit respectively).
