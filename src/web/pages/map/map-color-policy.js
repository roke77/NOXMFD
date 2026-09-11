// Stable signature for the MAP icon-color configuration. The telemetry frame creates a fresh
// object at 10 Hz, so identity cannot tell whether colors actually changed; sorting type keys
// makes equivalent registries compare equal regardless of dictionary enumeration order.
(function (root) {
  function signature(colors) {
    const value = colors || {};
    const types = value.types || {};
    const entries = Object.keys(types).sort().map((key) => {
      const item = types[key] || {};
      return [key, item.hex || '', item.f == null ? null : item.f];
    });
    return JSON.stringify([value.n || '', value.f || '', value.e || '', entries]);
  }

  const api = { signature };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.MapColorPolicy = api;
})(typeof window !== 'undefined' ? window : globalThis);
