using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using IOPath = System.IO.Path;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;

using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

namespace HideoutPosters;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "praedlab.hideoutposters.server";
    public string Name { get; init; } = "Hideout Posters";
    public string Author { get; init; } = "PraED_TV";
    public List<string> Contributors { get; init; } = new();
    public Version Version { get; init; } = new("0.5.6");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string> Incompatibilities { get; init; } = new();
    public Dictionary<string, Range> ModDependencies { get; init; } = new();
    public string Url { get; init; } = "https://github.com/PraED-lab";
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class HideoutPostersMod(
    ISptLogger<HideoutPostersMod> logger,
    CustomItemService customItemService,
    TradersTable tradersTable,
    TemplateTable templateTable) : IOnLoad
{
    const string FlyerParent = "6759673c76e93d8eb20b2080";
    const string PosterHandbook = "5b47574386f77428ca22b2f4";
    const string ArenaPoster = "675a0288c3102563bd01c9c3";
    const string Ragman = "5ac3b934156ae10c4430e83c";
    const string Roubles = "5449016a4bdc2d6f028b456f";
    const string LegacyPosterId = "6a1b0c0de0000000000a0001";   // le tout premier poster : garde pour ne pas casser les profils
    static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".gif" };

    // meme calcul que le client : nom du fichier (sans extension, en minuscules) -> identifiant fixe
    public static string IdFor(string stem, char kind)
    {
        using var md5 = MD5.Create();
        var h = md5.ComputeHash(Encoding.UTF8.GetBytes(stem.ToLowerInvariant()));
        var hex = string.Concat(h.Select(b => b.ToString("x2")));
        return "6a1b0c0de" + kind + hex.Substring(0, 14);
    }

    static string Pretty(string stem)
    {
        var s = stem.Replace('_', ' ').Replace('-', ' ').Trim();
        return s.Length == 0 ? "Poster" : char.ToUpper(s[0]) + s.Substring(1);
    }

    static string FindImageFolder(string modDir)
    {
        var d = new DirectoryInfo(modDir);
        for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
        {
            var c = IOPath.Combine(d.FullName, "BepInEx", "config", "HideoutPosters");
            if (Directory.Exists(c)) return c;
        }
        return null;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var modDir = IOPath.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var folder = FindImageFolder(modDir);
            var current = new List<string>();
            if (folder == null) logger.Warning("[Hideout Posters] dossier BepInEx/config/HideoutPosters introuvable : seul le poster d'origine sera cree");
            else current = Directory.GetFiles(folder).Where(f => Exts.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    .Select(f => IOPath.GetFileNameWithoutExtension(f)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            // on se souvient de toutes les images deja vues : un poster accroche dont l'image a ete retiree reste valide (sinon le profil plante)
            var knownFile = IOPath.Combine(modDir, "known_posters.txt");
            var known = File.Exists(knownFile) ? File.ReadAllLines(knownFile).Where(l => l.Trim().Length > 0).ToList() : new List<string>();
            foreach (var c in current) if (!known.Any(k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase))) known.Add(c);
            try { File.WriteAllLines(knownFile, known); } catch (Exception e) { logger.Warning("[Hideout Posters] memoire des posters : " + e.Message); }

            var ids = new List<MongoId> { new MongoId(LegacyPosterId) };
            Create(LegacyPosterId, "hp_poster_1", current.Count > 0 ? Pretty(current[0]) : "Poster PraED");
            foreach (var stem in known)
            {
                var id = IdFor(stem, '1');
                if (Create(id, "hp_" + stem, Pretty(stem))) ids.Add(new MongoId(id));
            }

            int slots = 0;
            foreach (var t in templateTable.Items.Values)
            {
                if (t?.Properties?.Slots == null) continue;
                foreach (var sl in t.Properties.Slots)
                {
                    if (sl?.Properties?.Filters == null) continue;
                    foreach (var f in sl.Properties.Filters)
                    {
                        if (f?.Filter == null || !f.Filter.Contains(new MongoId(ArenaPoster))) continue;
                        foreach (var id in ids) if (!f.Filter.Contains(id)) { f.Filter.Add(id); slots++; }
                    }
                }
            }

            var trader = tradersTable.GetTrader(new MongoId(Ragman));
            if (trader?.Assort == null) { logger.Error("[Hideout Posters] Ragman introuvable"); return Task.CompletedTask; }
            var a = trader.Assort;
            int sold = 0;
            // mode collection (reglage du client) : Ragman ne vend que les affiches debloquees en raid
            var forSale = current;
            bool collection = false;
            if (folder != null)
            {
                try
                {
                    var cfgDir = IOPath.GetDirectoryName(folder);
                    var cfg = IOPath.Combine(cfgDir, "praedlab.hideoutposters.cfg");
                    if (File.Exists(cfg))
                    {
                        string section = "";
                        foreach (var raw in File.ReadAllLines(cfg))
                        {
                            var l = raw.Trim();
                            if (l.StartsWith("[")) { section = l; continue; }
                            if (section.Equals("[Collection]", StringComparison.OrdinalIgnoreCase) && l.Replace(" ", "").StartsWith("Collectionmode=", StringComparison.OrdinalIgnoreCase))
                                collection = l.EndsWith("true", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    if (collection)
                    {
                        var uf = IOPath.Combine(cfgDir, "HideoutPosters_unlocked.txt");
                        var un = File.Exists(uf) ? File.ReadAllLines(uf).Select(x => x.Trim()).Where(x => x.Length > 0 && x.IndexOfAny(new[] { '/', '\\' }) < 0).Select(x => IOPath.GetFileNameWithoutExtension(x)).ToList() : new List<string>();
                        forSale = current.Where(c => un.Any(u => string.Equals(u, c, StringComparison.OrdinalIgnoreCase))).ToList();
                    }
                }
                catch (Exception e) { logger.Warning("[Hideout Posters] mode collection : " + e.Message); }
            }
            foreach (var stem in forSale)
            {
                var aid = new MongoId(IdFor(stem, '2'));
                a.Items.Add(new Item { Id = aid, Template = new MongoId(IdFor(stem, '1')), ParentId = "hideout", SlotId = "hideout", Upd = new Upd { UnlimitedCount = true, StackObjectsCount = 999999 } });
                a.BarterScheme[aid] = new List<List<BarterScheme>> { new() { new BarterScheme { Count = 1000, Template = new MongoId(Roubles) } } };
                a.LoyalLevelItems[aid] = 1;
                sold++;
            }
            logger.Success("[Hideout Posters] 0.4.8 : " + sold + " poster(s) en vente chez Ragman" + (collection ? " (mode collection : " + forSale.Count + "/" + current.Count + " debloquee(s))" : "") + " (" + string.Join(", ", forSale) + "), " + (known.Count + 1) + " poster(s) connus, " + slots + " autorisation(s) d'emplacement");
        }
        catch (Exception e) { logger.Error("[Hideout Posters] " + e); }
        return Task.CompletedTask;
    }

    bool Create(string id, string internalName, string name)
    {
        var locales = new Dictionary<string, LocaleDetails>
        {
            ["en"] = new LocaleDetails { Name = name, ShortName = name.Length > 12 ? name.Substring(0, 12) : name, Description = "A poster for your hideout: " + name + "." },
            ["fr"] = new LocaleDetails { Name = name, ShortName = name.Length > 12 ? name.Substring(0, 12) : name, Description = "Un poster pour ta planque : " + name + "." }
        };
        var res = customItemService.CreateItemFromClone(new NewItemFromCloneDetails
        {
            ItemTplToClone = new MongoId(ArenaPoster),
            ParentId = new MongoId(FlyerParent),
            NewId = new MongoId(id),
            NewItemName = internalName,
            FleaPriceRoubles = 1000,
            HandbookPriceRoubles = 1000,
            HandbookParentId = PosterHandbook,
            Locales = locales
        }, Assembly.GetExecutingAssembly());
        if (res == null || !res.Success) { logger.Error("[Hideout Posters] creation de " + name + " : " + (res?.Errors != null ? string.Join(", ", res.Errors) : "echec")); return false; }
        return true;
    }
}

public record RewardRequest : IRequestData
{
    public int tier { get; set; }
    public string? label { get; set; }
    public int qty { get; set; } = 1;
    public string? tpl { get; set; }
    public string? lang { get; set; }
    public bool preview { get; set; }
}

// Range Day : le client demande une recompense, le serveur tire un objet du jeu dans le bon palier et l'envoie par message
[Injectable]
public class RangeDayRewards(ISptLogger<RangeDayRewards> logger, TemplateTable templates, SPTarkov.Server.Core.Helpers.Profile.HandbookHelper handbook, SPTarkov.Server.Core.Services.Commerce.MailSendService mail, JsonUtil json)
{
    static readonly double[] Lo = { 0, 3000, 15000, 40000, 120000, 450000 }, Hi = { 3000, 15000, 40000, 120000, 450000, 5000000 };
    static readonly string[] TierName = { "ABSURDE", "COMMUN", "PEU COMMUN", "RARE", "ÉPIQUE", "LÉGENDAIRE" };
    readonly Dictionary<int, Queue<MongoId>> recent = new();
    static readonly string[] TierEn = { "ABSURD", "COMMON", "UNCOMMON", "RARE", "EPIC", "LEGENDARY" };
    static readonly string[] TauntsEn = {
        "you got what you deserve.", "even a blind Scav does better.", "the range filed a complaint.", "here's your prize, champ.",
        "the targets are still laughing at you.", "keep this, it's more useful than your gun.", "Ragman himself laughed.",
        "did your bullets go on vacation?", "this consolation prize is already too generous." };
    static readonly string[] Taunts = {
        "t'as eu ce que tu mérites.", "même un Scav aveugle fait mieux.", "le stand a porté plainte.", "voilà ta récompense, champion.",
        "les cibles se moquent encore de toi.", "c'est pas la taille du flingue qui compte… apparemment si.", "garde ça, ça te servira plus que ton arme.",
        "Ragman lui-même a ri.", "tes balles sont parties en vacances ?", "ce lot de consolation est déjà trop généreux." };
    readonly Random rng = new();
    List<MongoId>[]? pools;

    void Build()
    {
        pools = new List<MongoId>[6]; for (int i = 0; i < 6; i++) pools[i] = new List<MongoId>();
        foreach (var kv in templates.Items)
        {
            var t = kv.Value; var p = t?.Properties;
            if (t == null || p == null || t.Type != "Item") continue;
            if (p.QuestItem == true || (p.Slots != null && p.Slots.Any()) || (p.StackSlots != null && p.StackSlots.Any())) continue;
            if (p.CanSellOnRagfair == false) continue;
            double price; try { price = handbook.GetTemplatePrice(kv.Key); } catch { continue; }
            if (price <= 1) continue;
            for (int i = 0; i < 6; i++) if (price >= Lo[i] && price < Hi[i]) { pools[i].Add(kv.Key); break; }
        }
        logger.Info("[Hideout Posters] Range Day : objets par palier " + string.Join(" / ", pools.Select(x => x.Count)));
    }

    public string Give(RewardRequest req, MongoId session)
    {
        try
        {
            if (pools == null) Build();
            int tier = Math.Clamp(req.tier, 0, 5);
            var pool = pools![tier]; if (pool.Count == 0) return "{\"ok\":false}";
            MongoId tpl;
            if (!string.IsNullOrEmpty(req.tpl) && MongoId.IsValidMongoId(req.tpl) && templates.Items.ContainsKey(new MongoId(req.tpl))) tpl = new MongoId(req.tpl);
            else
            {
                // pas le meme objet avant longtemps
                if (!recent.TryGetValue(tier, out var q)) recent[tier] = q = new Queue<MongoId>();
                tpl = pool[rng.Next(pool.Count)];
                for (int t = 0; t < 12 && q.Contains(tpl); t++) tpl = pool[rng.Next(pool.Count)];
                q.Enqueue(tpl); while (q.Count > Math.Min(40, pool.Count / 2)) q.Dequeue();
            }
            if (req.preview) return "{\"ok\":true,\"tpl\":\"" + tpl + "\"}";
            var items = new List<Item>();
            for (int n = 0; n < Math.Clamp(req.qty, 1, 5); n++) items.Add(new Item { Id = new MongoId(), Template = tpl, Upd = new Upd { StackObjectsCount = 1 } });
            bool en = !string.IsNullOrEmpty(req.lang) && req.lang != "fr";
            string msg = (en ? "Range Day: reward " + TierEn[tier] : "Range Day : récompense " + TierName[tier]) + (string.IsNullOrEmpty(req.label) ? "" : " (" + req.label + ")");
            if (tier == 0) msg = "Range Day · " + (req.label ?? "") + (en ? ": " + TauntsEn[rng.Next(TauntsEn.Length)] : " : " + Taunts[rng.Next(Taunts.Length)]);
            mail.SendSystemMessageToPlayer(session, msg, items);
            logger.Info("[Hideout Posters] Range Day : recompense " + TierName[tier] + " -> " + tpl);
            return "{\"ok\":true,\"tpl\":\"" + tpl + "\"}";
        }
        catch (Exception e) { logger.Error("[Hideout Posters] Range Day : " + e.Message); return "{\"ok\":false}"; }
    }
}

[Injectable(TypePriority = OnLoadOrder.Routers)]
public class RangeDayRouter(JsonUtil jsonUtil, RangeDayRewards rewards) : StaticRouter(jsonUtil,
[
    new RouteAction<RewardRequest>("/hideoutposters/reward", async (url, info, sessionID, output, ct) => await new ValueTask<string>(rewards.Give(info, sessionID)))
]) { }
