# HarfBuzz

Just placing the compiled libraries.

Now: [v10.1.0](https://github.com/harfbuzz/harfbuzz/releases/tag/10.1.0)

## Compile

```
meson -Dtests=disabled -Dintrospection=disabled -Ddocs=disabled -Dexperimental_api=true -Db_vscrt=static_from_buildtype -Dbuildtype=release build/win-x64
meson --default-library=static -Dtests=disabled -Dintrospection=disabled -Ddocs=disabled -Dexperimental_api=true -Db_vscrt=static_from_buildtype -Dbuildtype=release build/win-x64-static
meson compile -C build/win-x64
```