using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NOXMFD
{
    internal static class TelemetryHttpRouter
    {
        internal static void Route(HttpListenerContext ctx, string path, CancellationToken ct)
        {
            if (path == "/stream")
                _ = Task.Run(() => TelemetryServer.HandleSseAsync(ctx, ct));
            else if (path == "/tgp.mjpg")
                _ = Task.Run(() => TelemetryServer.HandleMjpegAsync(ctx, ct));
            else if (path == "/map" || path == "/map.png" || path == "/map.jpg")
                TelemetryServer.ServeMap(ctx);
            else if (path == "/icon")
                TelemetryServer.ServeIcon(ctx);
            else if (path == "/weapon")
                TelemetryServer.ServeWeaponIcon(ctx);
            else if (path == "/cm")
                TelemetryServer.ServeCmIcon(ctx);
            else if (path == "/tgt-icon")
                TelemetryServer.ServeTgtIcon(ctx);
            else if (path == "/bdf-icon")
                TelemetryServer.ServeBdfIcon(ctx);
            else if (path == "/building-icon")
                TelemetryServer.ServeBuildingIcon(ctx);
            else if (path == "/hud-cat-icon")
                TelemetryServer.ServeHudCategoryIcon(ctx);
            else if (path == "/airframe")
                TelemetryServer.ServeAirframeImage(ctx);
            else if (path == "/airframe-layout")
                TelemetryServer.ServeAirframeLayout(ctx);
            else if (path == "/config")
                TelemetryServer.ServeConfig(ctx);
            else if (path == "/hud-options")
                TelemetryServer.ServeHudOptions(ctx);
            else if (path == "/wpt-options")
                TelemetryServer.ServeWptOptions(ctx);
            else if (path == "/layout-options")
                TelemetryServer.ServeLayoutOptions(ctx);
            else if (path == "/hud-presets")
                TelemetryServer.ServeHudPresets(ctx);
            else if (path == "/rates-config")
                TelemetryServer.ServeRatesConfig(ctx);
            else if (path == "/keybinds-config")
                TelemetryServer.ServeKeybindsConfig(ctx);
            else if (path == "/soi-instances")
                TelemetryServer.ServeSoiInstances(ctx);
            else if (path == "/ext-manifest")
                TelemetryServer.ServeExtManifest(ctx);
            else if (path.StartsWith("/ext/", StringComparison.Ordinal))
                TelemetryServer.HandleExtRequest(ctx, path);
            else if (path.StartsWith("/assets/", StringComparison.Ordinal))
                TelemetryAssets.ServeAsset(ctx, path);
            else if (path == "/map-view")
                TelemetryAssets.ServeAssetRel(ctx, "pages/map/map.html");
            else if (path == "/main")
                TelemetryAssets.ServeAssetRel(ctx, "pages/main/main.html");
            else if (path == "/avn")
                TelemetryAssets.ServeAssetRel(ctx, "pages/avn/avn.html");
            else if (path == "/afm")
                TelemetryAssets.ServeAssetRel(ctx, "pages/afm/afm.html");
            else if (path == "/tgp")
                TelemetryAssets.ServeAssetRel(ctx, "pages/tgp/tgp.html");
            // Static placeholder for EXT with nothing installed. Distinct from the /ext/<id>/*
            // prefix above, which needs the trailing slash to match.
            else if (path == "/ext")
                TelemetryAssets.ServeAssetRel(ctx, "pages/ext/ext.html");
            else if (path == "/wpn")
                TelemetryAssets.ServeAssetRel(ctx, "pages/wpn/wpn.html");
            else if (path == "/rwr")
                TelemetryAssets.ServeAssetRel(ctx, "pages/rwr/rwr.html");
            else if (path == "/rdr")
                TelemetryAssets.ServeAssetRel(ctx, "pages/rdr/rdr.html");
            else if (path == "/hsd")
                TelemetryAssets.ServeAssetRel(ctx, "pages/hsd/hsd.html");
            else if (path == "/tgt")
                TelemetryAssets.ServeAssetRel(ctx, "pages/tgt/tgt.html");
            else if (path == "/akf")
                TelemetryAssets.ServeAssetRel(ctx, "pages/akf/akf.html");
            else if (path == "/bdf")
                TelemetryAssets.ServeAssetRel(ctx, "pages/bdf/bdf.html");
            else if (path == "/mis")
                TelemetryAssets.ServeAssetRel(ctx, "pages/mis/mis.html");
            else if (path == "/obj")
                TelemetryAssets.ServeAssetRel(ctx, "pages/obj/obj.html");
            else if (path == "/wpt")
                TelemetryAssets.ServeAssetRel(ctx, "pages/wpt/wpt.html");
            else if (path == "/hud")
                TelemetryAssets.ServeAssetRel(ctx, "pages/hud/hud.html");
            else if (path == "/keybinds")
                TelemetryAssets.ServeAssetRel(ctx, "pages/keybinds/keybinds.html");
            else if (path == "/mapcfg")
                TelemetryAssets.ServeAssetRel(ctx, "pages/mapcfg/mapcfg.html");
            else if (path == "/tgpcfg")
                TelemetryAssets.ServeAssetRel(ctx, "pages/tgpcfg/tgpcfg.html");
            else if (path == "/command")
                TelemetryServer.HandleCommand(ctx);
            else if (path == "/mfd")
                TelemetryServer.Redirect(ctx, "/");
            else if (path == "/f35")
                TelemetryAssets.ServeAssetRel(ctx, "shell/f35/f35.html");
            else if (path == "/" || path == "/index.html")
                TelemetryAssets.ServeAssetRel(ctx, "shell/classic/mfd.html");
            else
                TelemetryServer.Redirect(ctx, "/");
        }
    }
}
