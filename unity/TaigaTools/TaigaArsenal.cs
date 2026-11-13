using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TaigaTools
{
    // Taiga Arsenal: Soviet / Russian munitions for Nuclear Option, as a separate Blueprinter mod. Each store is a stock weapon cloned
    // for its effects, networking and guidance, given its own model (tools/arsenal_gen.py -> TaigaSource/arsenal.json) and the real
    // weapon's mass, warhead, motor and seeker settings. Blueprinter ops offer them on the MiG-29 Fulcrum, MiG-29SMT and Su-30VXN mods when those
    // are installed (nothing of theirs is changed; without them Blueprinter logs one "aircraft not found" line per op).
    //   GSh-30-1 rounds  OFZ (HEI), OFZT (HE-T), BR-T (AP-T), BZT (API-T), RTP (practice tracer): one gun mount each, chosen on the
    //                    jet's cannon station
    //   air-to-air       R-27ER, R-27ET, R-74M
    //   air-to-surface   Kh-25ML, Kh-25MP, Kh-38ML, Kh-38MT, Kh-59M, Kh-35U
    //   bombs            KAB-500S-E, KAB-250LG-E, FAB-250, FAB-500, OFAB-250-270, ODAB-500PM, BetAB-500ShP, RBK-500
    //   rockets          B-8M1 (20 x S-8KOM), B-13L (5 x S-13OF), S-24B
    // and the rest of the MiG-29 munitions list (SecondBatch):
    //   GSh-30-1 rounds  ME (SAPHEI), MKETF (programmable airburst), OFZT-SR (HE-T self-destructing)
    //   air-to-air       R-77, R-24R, R-24T
    //   air-to-surface   Kh-29L, Kh-29T, Kh-29TD, Kh-38MK, Kh-31A, Kh-31P, Kh-59
    //   bombs            FAB-100, ZAB-500, KAB-500L, KAB-500Kr, KAB-250S-E, PBK-500U Drel, RBK-250, KMGU-2, RN-40
    //   rockets          UB-32 (32 x S-5M), S-25OF in its O-25 tube
    //   pods             MSP-418K jammer
    public static class TaigaArsenal
    {
        const string ModName = "taigaarsenal";
        const string ModDir = "Assets/Blueprinter/Mods/" + ModName;
        const string Dir = ModDir + "/weapons";
        const string OpsDir = ModDir + "/ops";
        const string SourceDir = "TaigaSource";
        const string GO = "Assets/Blueprinter/_donotship/GameObject/";
        const string MB = "Assets/Blueprinter/_donotship/MonoBehaviour/";
        const string BaseSkin = "Assets/Blueprinter/_donotship/Material/Multirole1_skin_PLACEHOLDER.mat";
        public const string DisplayName = "Taiga Arsenal";
        public const string Version = "1.0.0";

        // Each store is given station roles (the MiG's layout: cannon, centreline, inner, middle, outer); each supported aircraft maps
        // those roles to its own hardpoint set indices. A role an aircraft leaves out is not offered there, and a store whose mount key
        // starts with one of Skip is not offered on it at all.
        const int Gun = 0, Centre = 1, Inner = 2, Middle = 3, Outer = 4;
        class Jet
        {
            public string Key, Name;
            public int[][] Roles;           // indexed by role: the aircraft's hardpoint sets for it
            public string[] Skip = new string[0];
        }
        static readonly Jet[] Jets =
        {
            // MiG-29 Fulcrum and MiG-29SMT (WEAPON_MODS.md in the MiG repos): 0 cannon, 1 centreline, 2 inner, 3 middle, 4 outer
            new Jet { Key = "mig29_Fulcrum", Name = "MiG-29 Fulcrum", Roles = new[] { new[] { 0 }, new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 4 } } },
            new Jet { Key = "mig29smt_Fulcrum", Name = "MiG-29SMT", Roles = new[] { new[] { 0 }, new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 4 } } },
            // Su-30VXN Gandiva (Solar_Su30VXN 1.0.1): 0 GSh-30-1 cannon, 1 / 2 front / rear centreline (tandem between the engines),
            // 3 / 4 left / right intake pylons, 5 inner wing (3.29 m), 6 middle wing (4.55 m), 7 outer wing (6.10 m), 8 wingtips.
            new Jet
            {
                Key = "solar_su30", Name = "Su-30VXN Gandiva",
                Roles = new[] { new[] { 0 }, new[] { 1, 2 }, new[] { 3, 4, 5, 6 }, new[] { 6, 7 }, new[] { 8 } },
            },
        };
        const float InnerSlope = 2.5f, OuterSlope = 5f;

        [Serializable] class PartDump { public string name; public float[] vertices, normals, uvs; public int[] triangles; }
        [Serializable] class Dump { public PartDump[] parts; }

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        static Dictionary<string, Mesh> meshes;
        static Material mat;
        static readonly List<(UnityEngine.Object mount, int[] sets)> offers = new List<(UnityEngine.Object, int[])>();
        static int built;

        // ---------------- entry points ----------------

        [MenuItem("Taiga Arsenal/Build .nobp")]
        public static void BuildAll()
        {
            try
            {
                BuildAssets();
                var output = Environment.GetEnvironmentVariable("TAIGA_OUT");
                if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("TaigaBuild");
                Directory.CreateDirectory(output);
                Blueprinter.ModBuilder.Build(ModName, DisplayName, Version, output);
                Debug.Log("[Taiga] mod built -> " + output);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public static void BuildAssets()
        {
            AssetDatabase.DeleteAsset(ModDir);                       // afresh: nothing stale in the bundle
            AssetDatabase.CreateFolder("Assets/Blueprinter/Mods", ModName);
            AssetDatabase.CreateFolder(ModDir, "weapons");
            AssetDatabase.CreateFolder(ModDir, "ops");
            offers.Clear(); built = 0;
            var dump = JsonUtility.FromJson<Dump>(File.ReadAllText(Path.Combine(SourceDir, "arsenal.json")));
            meshes = dump.parts.ToDictionary(p => p.name, p => Save(ToMesh(p), $"{Dir}/taiga_{p.name}.asset"));
            mat = Save(ArsenalMaterial(), $"{Dir}/taiga_arsenal.mat");

            GunRounds();
            AirToAir();
            AirToSurface();
            Bombs();
            Rockets();
            SecondBatch();
            Offer();
            Blueprinter.ModInfo.Save(ModName, DisplayName, Version);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Taiga] {built} munitions built, {offers.Count} mounts offered on {Jets.Length} jets");
        }

        // ---------------- GSh-30-1 rounds ----------------

        // One gun mount per round type, each the stock internal 27 mm cannon retuned as the GSh-30-1 (150 rounds, ~1650 rpm), so the
        // MiG's cannon station (its hardpoint already puts the 27 mm's muzzle at the GSh-30-1 port) takes any of them. The round's
        // character comes from its info (armour piercing, explosive filler, velocity) and the gun's tracer and impact effects.
        static void GunRounds()
        {
            var he = GunEffects("gun_30mm_rotary_pod");              // small explosions on impact
            Round("ofz", "OFZ HEI", "OFZ", "30 mm OFZ high-explosive incendiary. A fuzed fragmentation shell with an incendiary filler: the most damage against aircraft and soft targets, no tracers.",
                velocity: 860f, pierce: 320f, blast: 0.12f, armorTier: 2f, tracerRatio: 999, tracer: new Color(14f, 5f, 1.5f), antiAir: 0.35f, antiSurface: 0.15f, effects: he);
            Round("ofzt", "OFZT HE-T", "OFZT", "30 mm OFZT high-explosive tracer. The OFZ shell with a tracer in every round, so you can walk your fire onto the target; slightly less filler.",
                velocity: 860f, pierce: 300f, blast: 0.10f, armorTier: 2f, tracerRatio: 1, tracer: new Color(16f, 4f, 1.2f), antiAir: 0.33f, antiSurface: 0.15f, effects: he);
            Round("brt", "BR-T AP-T", "BR-T", "30 mm BR armour-piercing tracer. A solid steel shot: punches through armoured vehicles and airframes, but has no explosive filler.",
                velocity: 900f, pierce: 1150f, blast: 0f, armorTier: 6f, tracerRatio: 1, tracer: new Color(18f, 2.5f, 1.5f), antiAir: 0.15f, antiSurface: 0.30f, effects: null);
            Round("bzt", "BZT API-T", "BZT", "30 mm BZT armour-piercing incendiary tracer. The BR shot with an incendiary charge that ignites fuel once it is through: armour and fuel tanks.",
                velocity: 890f, pierce: 950f, blast: 0.04f, armorTier: 5f, tracerRatio: 1, tracer: new Color(18f, 6f, 1f), antiAir: 0.22f, antiSurface: 0.28f, effects: null);
            Round("rtp", "RTP Practice", "RTP", "30 mm RTP practice tracer. An inert training round with the service rounds' ballistics and a bright tracer: aim practice without the cost of live rounds.",
                velocity: 860f, pierce: 60f, blast: 0f, armorTier: 0f, tracerRatio: 1, tracer: new Color(6f, 14f, 3f), antiAir: 0.05f, antiSurface: 0.02f, effects: null, cost: 0.2f);
        }

        // impact and self-destruct effects of a stock gun, to copy onto a round
        static UnityEngine.Object[] GunEffects(string donor)
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(GO + donor + "_PLACEHOLDER.prefab").GetComponentInChildren(T("Gun"), true);
            var so = new SerializedObject(g);
            return new[] { "impactEffectGround", "impactEffectArmor", "impactEffectWater", "selfDestructEffect" }.Select(f => so.FindProperty(f).objectReferenceValue).ToArray();
        }

        static void Round(string id, string name, string shortName, string desc, float velocity, float pierce, float blast, float armorTier, int tracerRatio,
            Color tracer, float antiAir, float antiSurface, UnityEngine.Object[] effects, float cost = 1f, float proximity = 0f, float selfDestruct = 0f)
        {
            var info = Clone("Gun27mm_Autocannon", $"taiga_GSh301_{id}_info");
            var iso = new SerializedObject(info);
            iso.FindProperty("weaponName").stringValue = $"GSh-30-1 {name}";
            iso.FindProperty("shortName").stringValue = $"30MM {shortName}";
            iso.FindProperty("description").stringValue = desc + " 150 rounds in the GSh-30-1 at about 1,650 rounds per minute: fire short bursts.";
            iso.FindProperty("muzzleVelocity").floatValue = velocity;
            iso.FindProperty("pierceDamage").floatValue = pierce;
            iso.FindProperty("blastDamage").floatValue = blast;
            iso.FindProperty("armorTierEffectiveness").floatValue = armorTier;
            iso.FindProperty("massPerRound").floatValue = 0.83f;
            iso.FindProperty("costPerRound").floatValue = iso.FindProperty("costPerRound").floatValue * cost;
            iso.FindProperty("effectiveness.antiAir").floatValue = antiAir;
            iso.FindProperty("effectiveness.antiSurface").floatValue = antiSurface;
            iso.FindProperty("targetRequirements.maxRange").floatValue = 1800f;
            iso.FindProperty("visibilityWhenFired").floatValue = 2500f;
            iso.ApplyModifiedPropertiesWithoutUndo();

            var go = Instance("gun_27mm_internal", $"taiga_GSh301_{id}");
            var gso = new SerializedObject(go.GetComponentInChildren(T("Gun"), true));
            gso.FindProperty("info").objectReferenceValue = info;
            gso.FindProperty("fireRate").floatValue = 1650f;
            gso.FindProperty("magazineCapacity").intValue = 150;
            gso.FindProperty("ammo").intValue = 150;
            gso.FindProperty("recoilImpulse").floatValue = 420f;
            gso.FindProperty("tracerRatio").intValue = tracerRatio;
            gso.FindProperty("tracerColor").colorValue = tracer;
            gso.FindProperty("tracerSize").floatValue = 2.1f;
            gso.FindProperty("bulletSpread").floatValue = 3f;
            if (proximity > 0f) gso.FindProperty("proximityTimer").floatValue = proximity;      // bursts beside a target, as the SPAAG's rounds
            if (selfDestruct > 0f) gso.FindProperty("bulletSelfDestruct").floatValue = selfDestruct;
            if (effects != null)
            {
                var f = new[] { "impactEffectGround", "impactEffectArmor", "impactEffectWater", "selfDestructEffect" };
                for (int i = 0; i < f.Length; i++) gso.FindProperty(f[i]).objectReferenceValue = effects[i];
            }
            gso.ApplyModifiedPropertiesWithoutUndo();
            var prefab = SavePrefab(go, $"{Dir}/taiga_GSh301_{id}.prefab");

            var m = Clone("gun_27mm_internal", $"taiga_GSh301_{id}_mount");
            var mso = new SerializedObject(m);
            mso.FindProperty("prefab").objectReferenceValue = prefab;
            mso.FindProperty("info").objectReferenceValue = info;
            mso.FindProperty("jsonKey").stringValue = $"taiga_gsh301_{id}";
            mso.FindProperty("mountName").stringValue = $"30mm GSh-30-1 {name} (150 rds)";
            mso.FindProperty("ammo").intValue = 150;
            mso.FindProperty("mass").floatValue = 150 * 0.83f;
            mso.ApplyModifiedPropertiesWithoutUndo();
            offers.Add((m, new[] { Gun }));
            built++;
        }

        // ---------------- air-to-air ----------------

        static void AirToAir()
        {
            // R-27ER: the R-27R's semi-active radar seeker on the bigger motor: about half as far again
            var er = Missile("AAM2", "R27ER", 4.78f, 0.13f, "SAM_Radar1", "SARHSeeker", so =>
            {
                so.FindProperty("mass").floatValue = 350f;
                so.FindProperty("finArea").floatValue = 0.75f;
                so.FindProperty("gLimit").floatValue = 25f;
                so.FindProperty("maxTurnRate").floatValue = 22f;
                Motor(so, 0, 62000f, 3.0f, 85f); Motor(so, 1, 19000f, 7.5f, 60f);
            }, s => { s.FindProperty("seekerAngle").floatValue = 50f; s.FindProperty("armDelay").floatValue = 1f; s.FindProperty("guidanceDelay").floatValue = 0.5f; s.FindProperty("lockPersistence").floatValue = 2f; });
            Store(er, "AAM2", "R-27ER", "R-27ER (AA-10 Alamo-C). The R-27R's semi-active radar seeker on a longer, larger motor: about half as far again. Keep the target locked with your radar until impact.",
                "AAM2_info", "Extended-range semi-active radar missile. Keep the target locked until impact.", 1500f, 75000f, 30f, 0.9f, 350f,
                "AAM2_single", "pylon", "aam2", "AKU470", -0.14f - 0.13f, InnerSlope, 0.95f, 440f, 80f, 0.08f, Inner, Middle);

            // R-27ET: infrared seeker, fire-and-forget, same big motor
            var et = Missile("AAM2", "R27ET", 4.50f, 0.13f, "AAM1", "IRSeeker", so =>
            {
                so.FindProperty("mass").floatValue = 343f;
                so.FindProperty("finArea").floatValue = 0.75f;
                so.FindProperty("gLimit").floatValue = 25f;
                so.FindProperty("maxTurnRate").floatValue = 22f;
                Motor(so, 0, 62000f, 3.0f, 85f); Motor(so, 1, 19000f, 7.5f, 60f);
            }, s => s.FindProperty("flareRejection").floatValue = 1.7f);
            Store(et, "AAM2", "R-27ET", "R-27ET (AA-10 Alamo-D). Extended-range infrared R-27: fire-and-forget once the seeker has the target's heat, and very hard to spot coming.",
                "AAM1_info", "Extended-range infrared missile. Lock the target's heat before launch; fire-and-forget.", 1000f, 45000f, 25f, 0.8f, 343f,
                "AAM2_single", "pylon", "aam2", "AKU470", -0.14f - 0.13f, InnerSlope, 0.95f, 433f, 80f, 0.08f, Inner, Middle);

            // R-74M: the R-73's successor; better seeker, wider off-boresight, more range
            var r74 = Missile("AAM1", "R74M", 2.90f, 0.085f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 110f;
                so.FindProperty("finArea").floatValue = 0.16f;
                so.FindProperty("gLimit").floatValue = 50f;
                so.FindProperty("maxTurnRate").floatValue = 95f;
                so.FindProperty("torque").floatValue = 4.2f;
                Motor(so, 0, 27000f, 4.0f, 42f, 15f);
            }, null, "IRSeeker", s => s.FindProperty("flareRejection").floatValue = 2.6f);
            var r74def = Store(r74, "AAM1", "R-74M", "R-74M (RVV-MD). The R-73's successor: a new multi-band infrared seeker that rejects flares better, a wider off-boresight angle and more range. Thrust vectoring.",
                "AAM1_info", "Short-range infrared missile, the R-73's successor. Better flare rejection; best inside 20 km.", 300f, 30000f, 60f, 0.45f, 110f,
                "AAM1_single", "pylon", "aam1", "APU73", -0.10f - 0.085f, InnerSlope, 1.05f, 150f, 40f, 0.04f, Inner, Middle);
            Mount(r74def, "R74M_outer", "AAM1_single", "pylon", "aam1", "APU73_O", "R74M", -0.10f - 0.085f, 2.90f, 0.085f, OuterSlope, 1.05f, "R-74M", 150f, 40f, 0.04f, Outer);
        }

        // ---------------- air-to-surface ----------------

        static void AirToSurface()
        {
            // Kh-25ML: light laser-guided missile (the stock AGM-48's airframe and agility, the laser rocket's seeker)
            var k25l = Missile("AGM1", "KH25ML", 3.70f, 0.1375f, "Rocket2", "LaserSeeker", so =>
            {
                so.FindProperty("mass").floatValue = 300f;
                so.FindProperty("blastYield").floatValue = 70f;
                so.FindProperty("pierceDamage").floatValue = 1500f;
                Motor(so, 0, 22000f, 6f, 85f);
            }, Laser(12f, 40f));
            var k25lInfo = Store(k25l, "AGM1", "Kh-25ML", "Kh-25ML (AS-10 Karen). Light laser-guided missile with a 90 kg warhead: vehicles, boats and small buildings out to about 10 km.",
                "info_AGM1", "Light laser-guided missile. Lock the target so it is lased, then fire: it homes on the laser spot.", 1000f, 10000f, 25f, 0.40f, 300f,
                "AGM1_single", "pylon", "agm1", "AKU58", -0.17f - 0.1375f - 0.018f, InnerSlope, 0.95f, 380f, 80f, 0.09f, Inner, Middle);
            SetLaserGuided(k25lInfo);

            // Kh-25MP: anti-radiation, passive radar seeker (stock ARAD-45 base)
            var k25p = Missile("ARM2", "KH25MP", 4.30f, 0.1375f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 315f;
                so.FindProperty("blastYield").floatValue = 60f;
                Motor(so, 0, 24000f, 6f, 90f);
            });
            Store(k25p, "ARM2", "Kh-25MP", "Kh-25MP (AS-12 Kegler). Light anti-radiation missile: homes on enemy radar emissions, for SAM sites at medium range.",
                "ARM2_info", "Light anti-radiation missile. Fire towards a radar emitter; it homes on its emissions.", 2000f, 40000f, 45f, 0.50f, 315f,
                "ARM2_single", "pylon", "agm1", "AKU58", -0.17f - 0.1375f - 0.018f, InnerSlope, 0.95f, 395f, 80f, 0.09f, Inner, Middle);

            // Kh-38ML: modular tactical missile, laser (stock AGM-68 airframe with the laser seeker)
            var k38l = Missile("AGM_heavy", "KH38ML", 4.20f, 0.155f, "Rocket2", "LaserSeeker", so =>
            {
                so.FindProperty("mass").floatValue = 520f;
                so.FindProperty("blastYield").floatValue = 150f;
                so.FindProperty("pierceDamage").floatValue = 2400f;
                Motor(so, 0, 40000f, 6f, 160f);
            }, Laser(15f, 40f));
            var k38lInfo = Store(k38l, "AGM_heavy", "Kh-38ML", "Kh-38ML. Modular tactical missile with a laser seeker and a 250 kg warhead, the Kh-25's replacement: out to about 40 km from height.",
                "info_AGM_heavy", "Laser-guided tactical missile, 250 kg warhead. Keep the target lased until impact.", 2000f, 40000f, 25f, 0.90f, 520f,
                "AGM_heavy_single", "pylon", "agm1", "AKU58", -0.17f - 0.155f - 0.018f, InnerSlope, 0.95f, 600f, 80f, 0.11f, Inner, Middle);
            SetLaserGuided(k38lInfo);

            // Kh-38MT: thermal-imaging seeker: the stock AGM-68's optical seeker, fire-and-forget
            var k38t = Missile("AGM_heavy", "KH38MT", 4.20f, 0.155f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 520f;
                so.FindProperty("blastYield").floatValue = 150f;
                so.FindProperty("pierceDamage").floatValue = 2400f;
                Motor(so, 0, 40000f, 6f, 160f);
            });
            Store(k38t, "AGM_heavy", "Kh-38MT", "Kh-38MT. The Kh-38 with a thermal-imaging seeker: fire-and-forget at the target your sensors have locked, day or night.",
                "info_AGM_heavy", "Thermal-imaging tactical missile, 250 kg warhead. Fire-and-forget on a locked target.", 2000f, 40000f, 25f, 1.00f, 520f,
                "AGM_heavy_single", "pylon", "agm1", "AKU58", -0.17f - 0.155f - 0.018f, InnerSlope, 0.95f, 600f, 80f, 0.11f, Inner, Middle);

            // Kh-59M: subsonic turbojet stand-off missile (stock ALM-C450 cruise missile base)
            var k59 = Missile("CruiseMissile1", "KH59M", 5.69f, 0.19f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 930f;
                so.FindProperty("blastYield").floatValue = 320f;
            });
            Store(k59, "CruiseMissile1", "Kh-59M", "Kh-59M (AS-18 Kazoo). Turbojet stand-off missile with a 320 kg warhead: flies low and subsonic to targets over 100 km away.",
                "info_CruiseMissile1", "Long-range stand-off missile. Launch at a locked target; it flies low to it.", 5000f, 115000f, 90f, 1.6f, 930f,
                "AShM2_single", "pylon", "ASHM2", "AKU58", -0.17f - 0.19f - 0.018f, InnerSlope, 0.95f, 1010f, 80f, 0.16f, Inner);

            // Kh-35U: sea-skimming anti-ship missile (stock AGM-99 base)
            var k35 = Missile("AShM2", "KH35U", 3.85f, 0.21f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 550f;
                so.FindProperty("blastYield").floatValue = 145f;
            });
            Store(k35, "AShM2", "Kh-35U", "Kh-35U (AS-20 Kayak). Turbofan anti-ship missile: skims the waves at near-sonic speed with a 145 kg warhead, out to about 130 km.",
                "AShM2_info", "Anti-ship missile. Launch at a ship; it cruises just above the sea to it.", 5000f, 130000f, 60f, 1.4f, 550f,
                "AShM2_single", "pylon", "ASHM2", "AKU58", -0.17f - 0.21f - 0.018f, InnerSlope, 0.95f, 630f, 80f, 0.14f, Inner);
        }

        static Action<SerializedObject> Laser(float angle, float maxSpeed) => s =>
        {
            s.FindProperty("maxSeekerAngle").floatValue = angle;
            s.FindProperty("errorRate").floatValue = 3f;
            s.FindProperty("maxTargetSpeed").floatValue = maxSpeed;
            s.FindProperty("selfDestructAtSpeed").floatValue = 60f;   // glides on after burnout
            s.FindProperty("guidanceDelay").floatValue = 0.5f;
        };

        // ---------------- bombs ----------------

        // Every stock bomb carries a bomb seeker that corrects onto a target near its aim point. The free-fall bombs keep it with a 3 m
        // search radius (the game has no seekerless bomb): they fall ballistically and only trim at the very end.
        static Action<SerializedObject> FreeFall(float armDelay) => s =>
        {
            s.FindProperty("searchRadius").floatValue = 3f;
            s.FindProperty("maxTargetSpeed").floatValue = 0f;
            s.FindProperty("armDelay").floatValue = armDelay;
        };

        static void Bombs()
        {
            // KAB-500S-E: satellite-guided 500 kg bomb: the stock glide bomb's guidance onto the locked target's position
            var k500 = Missile("bomb_500_glide", "KAB500S", 3.0f, 0.20f, null, null, so => so.FindProperty("mass").floatValue = 560f);
            Store(k500, "Bomb_500_glide", "KAB-500S-E", "KAB-500S-E. 500 kg satellite-guided bomb: it steers itself onto the locked target's position, fire-and-forget, in any weather.",
                "info_bomb_500_glide", "500 kg satellite-guided bomb. Lock a target and release within range.", 1000f, 15000f, 60f, 0.45f, 560f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.20f, 0f, 0f, 595f, 35f, 0.11f, Inner, Centre);

            // KAB-250LG-E: laser-guided 250 kg bomb (the stock 250 kg glide bomb with the laser seeker)
            var k250 = Missile("bomb_250_glide", "KAB250L", 3.2f, 0.1275f, "Rocket2", "LaserSeeker", so => so.FindProperty("mass").floatValue = 256f, s =>
            {
                s.FindProperty("maxSeekerAngle").floatValue = 20f;
                s.FindProperty("errorRate").floatValue = 4f;
                s.FindProperty("maxTargetSpeed").floatValue = 25f;
                s.FindProperty("selfDestructAtSpeed").floatValue = 0f;
                s.FindProperty("guidanceDelay").floatValue = 1.0f;
            });
            var k250Info = Store(k250, "Bomb_250_glide", "KAB-250LG-E", "KAB-250LG-E. 250 kg laser-guided bomb: light enough for the middle pylons; steers onto the laser spot.",
                "info_bomb_250_glide", "250 kg laser-guided bomb. Lock the target so it is lased and release in range.", 1000f, 12000f, 60f, 0.25f, 256f,
                "bomb_250_single", "pylon", "bomb", "BD3S", -0.14f - 0.1275f, 0f, 0f, 285f, 30f, 0.08f, Inner, Middle, Centre);
            SetLaserGuided(k250Info);

            // unguided M-62 bombs
            var f250 = Missile("bomb_250_1", "FAB250", 1.96f, 0.1625f, null, null, so => { so.FindProperty("mass").floatValue = 250f; so.FindProperty("blastYield").floatValue = 100f; },
                null, "OpticalSeekerBomb", FreeFall(3f));
            Store(f250, "Bomb_250_1", "FAB-250 M-62", "FAB-250 M-62. Unguided 250 kg high-explosive bomb, about 100 kg of filler: the Soviet air forces' everyday bomb. Aim it yourself.",
                "info_bomb_250_1", "Unguided 250 kg bomb. Falls where you release it.", 300f, 5000f, 90f, 0.04f, 250f,
                "bomb_250_single", "pylon", "bomb", "BD3S", -0.14f - 0.1625f, 0f, 0f, 280f, 30f, 0.09f, Inner, Middle, Centre);

            var f500 = Missile("bomb500", "FAB500", 2.47f, 0.20f, null, null, so => { so.FindProperty("mass").floatValue = 500f; so.FindProperty("blastYield").floatValue = 210f; },
                null, "OpticalSeekerBomb", FreeFall(4f));
            Store(f500, "Bomb500", "FAB-500 M-62", "FAB-500 M-62. Unguided 500 kg high-explosive bomb with about 200 kg of filler, for buildings and bridges.",
                "info_bomb_250_1", "Unguided 500 kg bomb. Falls where you release it.", 400f, 5000f, 90f, 0.07f, 500f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.20f, 0f, 0f, 535f, 35f, 0.12f, Inner, Centre);

            var of250 = Missile("bomb_250_1", "OFAB250", 1.48f, 0.1625f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 266f;
                so.FindProperty("blastYield").floatValue = 75f;
                so.FindProperty("pierceDamage").floatValue = 150f;
            }, null, "OpticalSeekerBomb", FreeFall(3f));
            Store(of250, "Bomb_250_1", "OFAB-250-270", "OFAB-250-270. Unguided 250 kg high-explosive fragmentation bomb: a thick, scored case that bursts into fragments against vehicles, troops and parked aircraft.",
                "info_bomb_250_1", "Unguided fragmentation bomb, for soft targets.", 300f, 5000f, 90f, 0.04f, 266f,
                "bomb_250_single", "pylon", "bomb", "BD3S", -0.14f - 0.1625f, 0f, 0f, 296f, 30f, 0.09f, Inner, Middle, Centre);

            // ODAB-500PM: fuel-air explosive (the stock demolition bomb's thermobaric blast)
            var odab = Missile("bomb_demolition", "ODAB500", 2.30f, 0.25f, null, null, so => so.FindProperty("mass").floatValue = 520f,
                null, "OpticalSeekerBomb", FreeFall(4f));
            Store(odab, "Bomb_demolition", "ODAB-500PM", "ODAB-500PM. Unguided 500 kg fuel-air explosive bomb: it bursts just above the ground into a cloud of fuel that detonates into a huge overpressure wave. Devastating against buildings and troops in the open.",
                "info_bomb_demolition", "Unguided thermobaric bomb, bursts just above the ground. A wide blast.", 500f, 5000f, 90f, 0.30f, 520f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.25f, 0f, 0f, 555f, 35f, 0.14f, Inner, Centre);

            // BetAB-500ShP: concrete-piercing, rocket-boosted (the stock bunker-buster)
            var betab = Missile("bomb_penetrator1", "BETAB500", 2.50f, 0.1625f, null, null, so => so.FindProperty("mass").floatValue = 424f,
                null, "OpticalSeekerBomb", FreeFall(4f));
            Store(betab, "Bomb_penetrator1", "BetAB-500ShP", "BetAB-500ShP. Concrete-piercing bomb: a parachute turns it nose-down, then a rocket drives it through runways and bunker roofs before it bursts.",
                "info_bomb_penetrator1", "Unguided penetrating bomb, for runways, bunkers and hardened shelters.", 300f, 5000f, 90f, 0.20f, 424f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.1625f, 0f, 0f, 460f, 35f, 0.11f, Inner, Centre);

            // RBK-500: cluster bomb (the stock cluster bomb's sub-munitions)
            var rbk = Missile("bomb_cluster_400", "RBK500", 2.50f, 0.225f, null, null, so => so.FindProperty("mass").floatValue = 500f,
                null, "OpticalSeekerBomb", FreeFall(4f));
            Store(rbk, "Bomb_cluster1", "RBK-500", "RBK-500. 500 kg cluster bomb: the casing opens over the target and scatters anti-armour bomblets that seek out the vehicles around it.",
                "info_Bomb_cluster1", "Cluster bomb with anti-armour bomblets, for groups of vehicles.", 500f, 5000f, 90f, 0.35f, 500f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.225f, 0f, 0f, 535f, 35f, 0.13f, Inner, Centre);
        }

        // ---------------- rockets ----------------

        // Unguided rockets: the stock laser rockets with their seeker cone closed to half a degree, so they fly where they are pointed.
        static Action<SerializedObject> Unguided => s =>
        {
            s.FindProperty("maxSeekerAngle").floatValue = 0.5f;
            s.FindProperty("errorRate").floatValue = 8f;
        };

        static void Rockets()
        {
            // S-8KOM in the B-8M1 pod (20 tubes)
            var s8 = Missile("Rocket1", "S8", 1.57f, 0.04f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 11.3f;
                so.FindProperty("blastYield").floatValue = 1.5f;
                so.FindProperty("pierceDamage").floatValue = 1300f;           // shaped charge, 400 mm of armour
                Motor(so, 0, 4500f, 0.9f, 3.6f);
            }, null, "LaserSeeker", Unguided);
            var s8Info = Store(s8, "Rocket1", "S-8KOM", "S-8KOM. 80 mm unguided rocket with a shaped-charge fragmentation warhead: armour and soft targets alike.",
                "info_rocket1", "80 mm unguided rockets. Point the aircraft at the target and ripple them.", 400f, 4000f, 15f, 0.006f, 11.3f, null, null, null, null, 0f, 0f, 0f, 0f, 0f, 0f);
            Pod("B8M1", "RocketPod1_single", s8Info, "S8", 1.57f, 0.04f, 2.75f, 0.26f, B8Tubes(), "B-8M1 (20x S-8KOM)", 20, 160f + 20 * 11.3f, 160f, 0.13f, Inner, Middle);

            // S-13OF in the B-13L pod (5 tubes)
            var s13 = Missile("Rocket2", "S13", 2.90f, 0.061f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 68f;
                so.FindProperty("blastYield").floatValue = 20f;
                so.FindProperty("pierceDamage").floatValue = 900f;
                Motor(so, 0, 15000f, 1.1f, 19f);
            }, null, "LaserSeeker", Unguided);
            var s13Info = Store(s13, "Rocket2", "S-13OF", "S-13OF. 122 mm unguided rocket with a heavy fragmentation warhead, for bunkers, vehicles and parked aircraft.",
                "info_rocket2", "122 mm unguided rockets. Point the aircraft at the target and fire.", 600f, 6000f, 15f, 0.03f, 68f, null, null, null, null, 0f, 0f, 0f, 0f, 0f, 0f);
            Pod("B13L", "Rocket2_4Pod", s13Info, "S13", 2.90f, 0.061f, 3.50f, 0.205f, B13Tubes(), "B-13L (5x S-13OF)", 5, 160f + 5 * 68f, 160f, 0.12f, Inner, Middle);

            // S-24B: heavy single rocket on an APU-68 rail
            var s24 = Missile("Rocket2", "S24B", 2.33f, 0.12f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 235f;
                so.FindProperty("blastYield").floatValue = 60f;
                so.FindProperty("pierceDamage").floatValue = 800f;
                Motor(so, 0, 42000f, 1.1f, 80f);
            }, null, "LaserSeeker", Unguided);
            Store(s24, "Rocket2", "S-24B", "S-24B. 240 mm heavy unguided rocket with a 123 kg fragmentation warhead, for hardened point targets.",
                "info_rocket2", "Heavy 240 mm unguided rocket. Point the aircraft at the target and fire.", 800f, 4000f, 15f, 0.08f, 235f,
                "AGM1_single", "pylon", "agm1", "APU68", -0.12f - 0.12f - 0.015f, InnerSlope, 0.85f, 280f, 45f, 0.07f, Inner, Middle);
        }

        static List<Vector2> B8Tubes()
        {
            var p = new List<Vector2> { Vector2.zero };
            for (int k = 0; k < 7; k++) p.Add(new Vector2(Mathf.Cos(2 * Mathf.PI * k / 7), Mathf.Sin(2 * Mathf.PI * k / 7)) * 0.085f);
            for (int k = 0; k < 12; k++) p.Add(new Vector2(Mathf.Cos(2 * Mathf.PI * k / 12 + 0.26f), Mathf.Sin(2 * Mathf.PI * k / 12 + 0.26f)) * 0.17f);
            return p;   // tools/arsenal_gen.py b8m1_tubes
        }

        static List<Vector2> B13Tubes() =>
            Enumerable.Range(0, 5).Select(k => new Vector2(Mathf.Cos(2 * Mathf.PI * k / 5 + Mathf.PI / 2), Mathf.Sin(2 * Mathf.PI * k / 5 + Mathf.PI / 2)) * 0.12f).ToList();

        // A rocket pod: the stock pod's mount, its pod body replaced by ours on a short rack, and one stowed rocket per tube (the stock
        // pod's rockets duplicated up to the tube count), each a MountedMissile launched from its tube.
        static void Pod(string id, string basePrefab, ScriptableObject rocketInfo, string rocketMesh, float rocketLen, float rocketR, float podLen, float podR,
            List<Vector2> tubes, string mountName, int count, float mass, float emptyMass, float drag, params int[] sets)
        {
            var go = Instance(basePrefab, $"taiga_{id}");
            var rootMf = go.GetComponent<MeshFilter>(); if (rootMf != null) rootMf.sharedMesh = null;
            float podY = -0.145f - podR;                                    // pod top against the rack's sway braces
            var rack = go.transform.Find("pylon");
            if (rack == null)
            {
                rack = new GameObject("pylon").transform; rack.SetParent(go.transform, false);
                rack.gameObject.AddComponent<MeshFilter>(); rack.gameObject.AddComponent<MeshRenderer>();
            }
            rack.localPosition = Vector3.zero; rack.localRotation = Quaternion.identity; rack.localScale = Vector3.one;
            rack.GetComponent<MeshFilter>().sharedMesh = meshes["BD3"]; rack.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
            var pod = go.transform.Find("pod");
            pod.localPosition = new Vector3(0, podY, 0); pod.localRotation = Quaternion.identity; pod.localScale = Vector3.one;
            pod.GetComponent<MeshFilter>().sharedMesh = meshes[id]; pod.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
            var slots = go.GetComponentsInChildren(T("MountedMissile"), true).Select(c => c.transform).ToList();
            while (slots.Count < count)
            {
                var copy = UnityEngine.Object.Instantiate(slots[0].gameObject, slots[0].parent);
                copy.name = $"rocket{slots.Count + 1}";
                slots.Add(copy.transform);
            }
            while (slots.Count > count) { UnityEngine.Object.DestroyImmediate(slots[slots.Count - 1].gameObject); slots.RemoveAt(slots.Count - 1); }
            for (int i = 0; i < count; i++)
            {
                var s = slots[i];
                // in its tube, nose just inside; placed in the mount's frame (the stock slots are children of the pod body)
                s.localScale = Vector3.one;
                s.SetPositionAndRotation(go.transform.TransformPoint(new Vector3(tubes[i].x, podY + tubes[i].y, podLen / 2 - rocketLen / 2 - 0.03f)), go.transform.rotation);
                var mf = s.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = meshes[rocketMesh];
                var mr = s.GetComponent<MeshRenderer>(); if (mr != null) mr.sharedMaterials = new[] { mat };
                foreach (var c in s.GetComponents<CapsuleCollider>()) { c.radius = rocketR; c.height = rocketLen; c.center = Vector3.zero; c.direction = 2; }
                var so = new SerializedObject(s.GetComponent(T("MountedMissile")));
                so.FindProperty("info").objectReferenceValue = rocketInfo;
                var rl = so.FindProperty("railLength"); if (rl != null) rl.floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var bc in pod.GetComponents<BoxCollider>()) { bc.center = Vector3.zero; bc.size = new Vector3(podR * 2, podR * 2, podLen); }
            var prefab = SavePrefab(go, $"{Dir}/taiga_{id}.prefab");
            var m = CloneMount(basePrefab, $"taiga_{id}_mount", prefab, rocketInfo, $"taiga_{id}_pod", mountName, mass, emptyMass, drag);
            var mso = new SerializedObject(m); mso.FindProperty("ammo").intValue = count; mso.ApplyModifiedPropertiesWithoutUndo();
            offers.Add((m, sets));
        }

        // ---------------- the rest of the MiG-29 munitions list ----------------

        static void SecondBatch()
        {
            // GSh-30-1 rounds
            var he = GunEffects("gun_30mm_rotary_pod");
            Round("me", "ME SAPHEI", "ME", "30 mm ME semi-armour-piercing high-explosive incendiary. A thick steel nose delays the burst until the shell is through the outer skin, so it bursts inside the target.",
                velocity: 860f, pierce: 650f, blast: 0.10f, armorTier: 3f, tracerRatio: 999, tracer: new Color(14f, 5f, 1.5f), antiAir: 0.32f, antiSurface: 0.22f, effects: he);
            Round("mketf", "MKETF Airburst", "MKETF", "30 mm MKETF programmable airburst. Each round bursts beside its target into a cloud of tungsten pellets, so a near miss still hits: made for drones and small aircraft, weak against armour.",
                velocity: 880f, pierce: 150f, blast: 0.08f, armorTier: 1f, tracerRatio: 4, tracer: new Color(12f, 9f, 2f), antiAir: 0.45f, antiSurface: 0.05f, effects: he, cost: 3f, proximity: 1f, selfDestruct: 4f);
            Round("ofztsr", "OFZT-SR HE-T", "OFZT-SR", "30 mm OFZT-SR high-explosive tracer with a self-destruct: the OFZT for training and over friendly ground, it bursts in the air after about 2.5 km instead of falling back to earth.",
                velocity: 860f, pierce: 300f, blast: 0.10f, armorTier: 2f, tracerRatio: 1, tracer: new Color(16f, 4f, 1.2f), antiAir: 0.33f, antiSurface: 0.12f, effects: he, cost: 0.6f, selfDestruct: 3f);

            // R-77: active radar, fire-and-forget (the stock AAM-36's seeker and airframe), on the short AKU-170
            var r77 = Missile("AAM4", "R77", 3.60f, 0.10f, null, null, so => { so.FindProperty("mass").floatValue = 175f; so.FindProperty("blastYield").floatValue = 22f; });
            Store(r77, "AAM4", "R-77", "R-77 (RVV-AE, AA-12 Adder). Active radar homing medium-range missile with lattice tail fins: fire-and-forget once its own radar has the target.",
                "AAM4_info", "Active radar medium-range missile. Fire-and-forget; it guides itself in the last part of its flight.", 1000f, 80000f, 30f, 1.0f, 175f,
                "AAM2_single", "pylon", "aam2", "AKU170", -0.10f - 0.10f, InnerSlope, 0.75f, 225f, 50f, 0.07f, Inner, Middle);

            // R-24R / R-24T: the MiG-23's missile, carried by the early prototypes: the R-27ER's recipe with a smaller motor
            var r24r = Missile("AAM2", "R24R", 4.46f, 0.115f, "SAM_Radar1", "SARHSeeker", so =>
            {
                so.FindProperty("mass").floatValue = 243f;
                so.FindProperty("gLimit").floatValue = 22f;
                Motor(so, 0, 45000f, 2.5f, 60f); Motor(so, 1, 14000f, 6f, 40f);
            }, s => { s.FindProperty("seekerAngle").floatValue = 45f; s.FindProperty("armDelay").floatValue = 1f; s.FindProperty("guidanceDelay").floatValue = 0.5f; s.FindProperty("lockPersistence").floatValue = 1.5f; });
            Store(r24r, "AAM2", "R-24R", "R-24R (AA-7 Apex). The MiG-23's semi-active radar missile, carried by the first MiG-29 prototypes. Keep the target locked with your radar until impact.",
                "AAM2_info", "Semi-active radar missile. Keep the target locked until impact.", 1500f, 50000f, 30f, 0.6f, 243f,
                "AAM2_single", "pylon", "aam2", "AKU470", -0.14f - 0.115f, InnerSlope, 0.95f, 323f, 80f, 0.08f, Inner, Middle);
            var r24t = Missile("AAM2", "R24T", 4.16f, 0.115f, "AAM1", "IRSeeker", so =>
            {
                so.FindProperty("mass").floatValue = 235f;
                so.FindProperty("gLimit").floatValue = 22f;
                Motor(so, 0, 45000f, 2.5f, 60f); Motor(so, 1, 14000f, 6f, 40f);
            }, s => s.FindProperty("flareRejection").floatValue = 0.9f);
            Store(r24t, "AAM2", "R-24T", "R-24T (AA-7 Apex). Infrared R-24: fire-and-forget, but an old seeker that flares fool easily.",
                "AAM1_info", "Infrared missile. Lock the target's heat before launch; fire-and-forget.", 1000f, 25000f, 25f, 0.5f, 235f,
                "AAM2_single", "pylon", "aam2", "AKU470", -0.14f - 0.115f, InnerSlope, 0.95f, 315f, 80f, 0.08f, Inner, Middle);

            // Kh-29: heavy tactical missiles with a 320 kg penetrating warhead, on the AKU-58
            void Kh29(string id, string name, string desc, string infoDesc, float range, float burn, float fuel, string seekerFrom, string seekerType, Action<SerializedObject> seeker, bool laser)
            {
                var go = Missile("AGM_heavy", id, 3.875f, 0.19f, seekerFrom, seekerType, so =>
                {
                    so.FindProperty("mass").floatValue = 680f;
                    so.FindProperty("blastYield").floatValue = 320f;
                    so.FindProperty("pierceDamage").floatValue = 3000f;
                    Motor(so, 0, 52000f, burn, fuel);
                }, seeker);
                var info = Store(go, "AGM_heavy", name, desc, "info_AGM_heavy", infoDesc, 2000f, range, 25f, 1.1f, 680f,
                    "AGM_heavy_single", "pylon", "agm1", "AKU58", -0.17f - 0.19f - 0.018f, InnerSlope, 0.95f, 760f, 80f, 0.14f, Inner);
                if (laser) SetLaserGuided(info);
            }
            Kh29("KH29L", "Kh-29L", "Kh-29L (AS-14 Kedge-A). Heavy laser-guided missile with a 320 kg penetrating warhead, for bunkers, bridges and ships: out to about 10 km.",
                "Heavy laser-guided missile. Keep the target lased until impact.", 10000f, 5f, 200f, "Rocket2", "LaserSeeker", Laser(15f, 30f), true);
            Kh29("KH29T", "Kh-29T", "Kh-29T (AS-14 Kedge-B). Heavy TV-guided missile: the seeker locks the target's picture before launch, then it is fire-and-forget. 320 kg warhead, about 12 km.",
                "Heavy TV-guided missile. Lock the target, fire-and-forget.", 12000f, 5f, 200f, null, null, null, false);
            Kh29("KH29TD", "Kh-29TD", "Kh-29TD. The Kh-29T with a longer-burning motor and a better seeker: about 30 km from height.",
                "Long-range heavy TV-guided missile. Lock the target, fire-and-forget.", 30000f, 7f, 260f, null, null, null, false);

            // Kh-38MK: satellite guidance (the stock satellite glide bomb's guidance onto the locked target's position)
            var k38k = Missile("AGM_heavy", "KH38MK", 4.20f, 0.155f, "bomb_500_glide", "*", so =>
            {
                so.FindProperty("mass").floatValue = 520f;
                so.FindProperty("blastYield").floatValue = 150f;
                so.FindProperty("pierceDamage").floatValue = 2400f;
                Motor(so, 0, 40000f, 6f, 160f);
            });
            Store(k38k, "AGM_heavy", "Kh-38MK", "Kh-38MK. The Kh-38 with satellite guidance: it flies to the locked target's position, fire-and-forget in any weather, but cannot follow a moving target.",
                "info_AGM_heavy", "Satellite-guided tactical missile, 250 kg warhead. For fixed targets; fire-and-forget.", 2000f, 40000f, 25f, 0.9f, 520f,
                "AGM_heavy_single", "pylon", "agm1", "AKU58", -0.17f - 0.155f - 0.018f, InnerSlope, 0.95f, 600f, 80f, 0.11f, Inner, Middle);

            // Kh-31A / Kh-31P: supersonic ramjet missiles (the stock AShM-300 and ARAD-116), on the AKU-58
            var k31a = Missile("AShM1", "KH31A", 4.70f, 0.18f, null, null, so => { so.FindProperty("mass").floatValue = 610f; so.FindProperty("blastYield").floatValue = 94f; });
            Store(k31a, "AShM1", "Kh-31A", "Kh-31A (AS-17 Krypton). Ramjet anti-ship missile at Mach 3.5 with an active radar seeker: very hard for a ship's defences to stop.",
                "info_AShM1", "Supersonic anti-ship missile. Launch at a ship; its own radar finds it at the end.", 5000f, 50000f, 60f, 1.3f, 610f,
                "AGM_heavy_single", "pylon", "agm1", "AKU58", -0.17f - 0.18f - 0.018f, InnerSlope, 0.95f, 690f, 80f, 0.14f, Inner);
            var k31p = Missile("ARM1", "KH31P", 5.23f, 0.18f, null, null, so => { so.FindProperty("mass").floatValue = 600f; so.FindProperty("blastYield").floatValue = 87f; });
            Store(k31p, "ARM1", "Kh-31P", "Kh-31P (AS-17 Krypton). Ramjet anti-radiation missile at Mach 3.5: homes on air-defence radars from over 100 km before they can react.",
                "ARM1_info", "Supersonic anti-radiation missile. Fire towards a radar emitter.", 3000f, 110000f, 45f, 1.4f, 600f,
                "AGM_heavy_single", "pylon", "agm1", "AKU58", -0.17f - 0.18f - 0.018f, InnerSlope, 0.95f, 680f, 80f, 0.15f, Inner);

            // Kh-59: the solid-motor original, TV-guided, about 40 km
            var k59 = Missile("AGM_heavy", "KH59", 5.37f, 0.19f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 760f;
                so.FindProperty("blastYield").floatValue = 147f;
                Motor(so, 0, 30000f, 9f, 230f);
            });
            Store(k59, "AGM_heavy", "Kh-59", "Kh-59 (AS-13 Kingbolt). The original Kh-59: a solid-motor TV-guided stand-off missile with a 147 kg warhead, out to about 40 km.",
                "info_AGM_heavy", "TV-guided stand-off missile. Lock the target, fire-and-forget.", 3000f, 40000f, 30f, 1.2f, 760f,
                "AGM_heavy_single", "pylon", "agm1", "AKU58", -0.17f - 0.19f - 0.018f, InnerSlope, 0.95f, 840f, 80f, 0.15f, Inner);

            // bombs
            var f100 = Missile("bomb_125_1", "FAB100", 1.07f, 0.134f, null, null, so => { so.FindProperty("mass").floatValue = 100f; so.FindProperty("blastYield").floatValue = 40f; },
                null, "*", FreeFall(2f));
            Store(f100, "Bomb_125_1", "FAB-100", "FAB-100. Unguided 100 kg high-explosive bomb, for light targets and for spreading several along a line.",
                "info_bomb_125_1", "Unguided 100 kg bomb. Falls where you release it.", 200f, 5000f, 90f, 0.02f, 100f,
                "bomb_250_single", "pylon", "bomb", "BD3S", -0.14f - 0.134f, 0f, 0f, 125f, 25f, 0.06f, Inner, Middle, Centre);

            var zab = Missile("bomb500", "ZAB500", 2.28f, 0.225f, null, null, so => { so.FindProperty("mass").floatValue = 480f; so.FindProperty("blastYield").floatValue = 120f; },
                null, "*", FreeFall(4f));
            Store(zab, "Bomb500", "ZAB-500", "ZAB-500. Unguided 500 kg incendiary bomb. The game has no lasting fire, so its burning mixture is modelled as a wide, weaker blast.",
                "info_bomb_250_1", "Unguided incendiary bomb, for soft targets and buildings.", 400f, 5000f, 90f, 0.06f, 480f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.225f, 0f, 0f, 515f, 35f, 0.13f, Inner, Centre);

            var k500l = Missile("bomb_500_glide", "KAB500L", 3.05f, 0.20f, "Rocket2", "LaserSeeker", so => so.FindProperty("mass").floatValue = 534f, s =>
            {
                s.FindProperty("maxSeekerAngle").floatValue = 20f;
                s.FindProperty("errorRate").floatValue = 4f;
                s.FindProperty("maxTargetSpeed").floatValue = 25f;
                s.FindProperty("selfDestructAtSpeed").floatValue = 0f;
                s.FindProperty("guidanceDelay").floatValue = 1.0f;
            });
            var k500lInfo = Store(k500l, "Bomb_500_glide", "KAB-500L", "KAB-500L. 500 kg laser-guided bomb with a 450 kg penetrating warhead: steers onto the laser spot for as long as the target is lased.",
                "info_bomb_500_glide", "500 kg laser-guided bomb. Lock the target so it is lased and release in range.", 1000f, 12000f, 60f, 0.40f, 534f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.20f, 0f, 0f, 570f, 35f, 0.11f, Inner, Centre);
            SetLaserGuided(k500lInfo);

            var k500kr = Missile("bomb_500_glide", "KAB500KR", 3.05f, 0.175f, null, null, so => so.FindProperty("mass").floatValue = 520f);
            Store(k500kr, "Bomb_500_glide", "KAB-500Kr", "KAB-500Kr. 500 kg TV-guided bomb: its seeker locks the target's picture before release, then it is fire-and-forget.",
                "info_bomb_500_glide", "500 kg TV-guided bomb. Lock a target and release within range.", 1000f, 15000f, 60f, 0.45f, 520f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.175f, 0f, 0f, 555f, 35f, 0.11f, Inner, Centre);

            var k250s = Missile("bomb_250_glide", "KAB250S", 2.90f, 0.1275f, null, null, so => so.FindProperty("mass").floatValue = 250f);
            Store(k250s, "Bomb_250_glide", "KAB-250S-E", "KAB-250S-E. 250 kg satellite-guided bomb: the KAB-500S-E's guidance in a bomb light enough for the middle pylons.",
                "info_bomb_250_glide", "250 kg satellite-guided bomb. Lock a target and release within range.", 1000f, 12000f, 60f, 0.25f, 250f,
                "bomb_250_single", "pylon", "bomb", "BD3S", -0.14f - 0.1275f, 0f, 0f, 280f, 30f, 0.08f, Inner, Middle, Centre);

            // PBK-500U: a cluster bomb steered by the satellite glide bomb's guidance
            var pbk = Missile("bomb_cluster_400", "PBK500U", 3.08f, 0.19f, "bomb_500_glide", "*", so => so.FindProperty("mass").floatValue = 300f);
            Store(pbk, "Bomb_cluster1", "PBK-500U Drel", "PBK-500U Drel. 300 kg satellite-guided gliding cluster bomb: it steers to the locked target and scatters anti-armour bomblets over it.",
                "info_Bomb_cluster1", "Guided cluster bomb with anti-armour bomblets. Lock a group of vehicles and release in range.", 1000f, 15000f, 60f, 0.60f, 300f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.17f, 0f, 0f, 335f, 35f, 0.12f, Inner, Centre);

            var rbk250 = Missile("bomb_cluster_400", "RBK250", 2.12f, 0.1625f, null, null, so => so.FindProperty("mass").floatValue = 273f,
                null, "*", FreeFall(3f));
            Store(rbk250, "Bomb_cluster1", "RBK-250", "RBK-250. 250 kg cluster bomb: the RBK-500's anti-armour bomblets in a bomb light enough for the middle pylons.",
                "info_Bomb_cluster1", "Cluster bomb with anti-armour bomblets, for groups of vehicles.", 400f, 5000f, 90f, 0.20f, 273f,
                "bomb_250_single", "pylon", "bomb", "BD3S", -0.14f - 0.1625f, 0f, 0f, 303f, 30f, 0.10f, Inner, Middle, Centre);

            var kmgu = Missile("bomb_cluster_400", "KMGU2", 3.0f, 0.20f, null, null, so => so.FindProperty("mass").floatValue = 525f,
                null, "*", FreeFall(1f));
            Store(kmgu, "Bomb_cluster1", "KMGU-2", "KMGU-2. Sub-munition dispenser. The real one stays on the jet and drops its bomblets from low level; here the container is released and opens straight away, spreading them along your path.",
                "info_Bomb_cluster1", "Bomblet dispenser for low passes over vehicles.", 200f, 3000f, 90f, 0.30f, 525f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.20f, 0f, 0f, 560f, 35f, 0.13f, Inner, Centre);

            // RN-40: the stock tactical nuclear bomb (1.5 kt, the game's own fuzing and parachute)
            var rn40 = Missile("nuclearBomb1", "RN40", 3.0f, 0.20f, null, null, so => so.FindProperty("mass").floatValue = 400f);
            Store(rn40, "NuclearBomb1", "RN-40", "RN-40. Soviet free-fall tactical nuclear bomb, set to a low 1.5 kt yield. Arms after 8 seconds and below 150 m; dropped below 300 m it lays down under a parachute.",
                "info_nuclearBomb1", "Tactical nuclear bomb, 1.5 kt. Release and climb away.", 2000f, 8000f, 90f, 28f, 400f,
                "bomb_500_single", "pylon", "bomb", "BD3", -0.14f - 0.20f, 0f, 0f, 435f, 35f, 0.12f, Centre);

            // rockets
            var s5 = Missile("Rocket1", "S5", 0.882f, 0.0285f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 3.86f;
                so.FindProperty("blastYield").floatValue = 0.4f;
                so.FindProperty("pierceDamage").floatValue = 300f;
                Motor(so, 0, 2000f, 0.7f, 1.2f);
            }, null, "LaserSeeker", Unguided);
            var s5Info = Store(s5, "Rocket1", "S-5M", "S-5M. 57 mm unguided rocket with a small fragmentation warhead: light, but the UB-32 fires 32 of them in a salvo.",
                "info_rocket1", "57 mm unguided rockets. Point the aircraft at the target and ripple them.", 300f, 3000f, 15f, 0.003f, 3.86f, null, null, null, null, 0f, 0f, 0f, 0f, 0f, 0f);
            Pod("UB32", "RocketPod1_single", s5Info, "S5", 0.882f, 0.0285f, 2.00f, 0.205f, UB32Tubes(), "UB-32 (32x S-5M)", 32, 110f + 32 * 3.86f, 110f, 0.12f, Inner, Middle);

            var s25 = Missile("Rocket2", "S25", 3.31f, 0.17f, null, null, so =>
            {
                so.FindProperty("mass").floatValue = 381f;
                so.FindProperty("blastYield").floatValue = 150f;
                so.FindProperty("pierceDamage").floatValue = 1000f;
                Motor(so, 0, 60000f, 1.1f, 100f);
            }, null, "LaserSeeker", Unguided);
            Store(s25, "Rocket2", "S-25OF", "S-25OF. 340 mm unguided rocket with a 150 kg fragmentation warhead, fired from its own O-25 tube: the heaviest rocket a MiG carries.",
                "info_rocket2", "Heavy 340 mm unguided rocket. Point the aircraft at the target and fire.", 1000f, 4000f, 15f, 0.12f, 381f,
                "AGM1_single", "pylon", "agm1", "O25", -0.235f + 0.015f, 0f, 0f, 480f, 100f, 0.12f, Inner);

            // MSP-418K: the stock ECM pod's jammer with our pod body
            EcmPod("MSP418K", "MSP-418K jamming pod", 150f, 0.03f, Middle, Outer);
        }

        static List<Vector2> UB32Tubes()
        {
            var p = new List<Vector2>();
            for (int k = 0; k < 4; k++) p.Add(new Vector2(Mathf.Cos(2 * Mathf.PI * k / 4 + 0.785f), Mathf.Sin(2 * Mathf.PI * k / 4 + 0.785f)) * 0.045f);
            for (int k = 0; k < 10; k++) p.Add(new Vector2(Mathf.Cos(2 * Mathf.PI * k / 10), Mathf.Sin(2 * Mathf.PI * k / 10)) * 0.11f);
            for (int k = 0; k < 18; k++) p.Add(new Vector2(Mathf.Cos(2 * Mathf.PI * k / 18 + 0.17f), Mathf.Sin(2 * Mathf.PI * k / 18 + 0.17f)) * 0.172f);
            return p;   // tools/arsenal_gen.py ub32_tubes
        }

        // An ECM pod: the stock ECM pod (its jammer and power) with our body; the mesh hangs below the station (top at y = 0).
        static void EcmPod(string id, string mountName, float mass, float drag, params int[] sets)
        {
            var go = Instance("ECMPod1", $"taiga_{id}");
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.transform != go.transform) mf.sharedMesh = null;
            var root = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>(); root.sharedMesh = meshes[id];
            var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { mat };
            var b = meshes[id].bounds;
            foreach (var bc in go.GetComponentsInChildren<BoxCollider>(true)) { bc.center = b.center; bc.size = b.size; }
            var prefab = SavePrefab(go, $"{Dir}/taiga_{id}.prefab");
            var m = Clone("ECMPod1", $"taiga_{id}_mount");
            var so = new SerializedObject(m);
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("jsonKey").stringValue = $"taiga_{id.ToLower()}";
            so.FindProperty("mountName").stringValue = mountName;
            so.FindProperty("mass").floatValue = mass;
            so.FindProperty("emptyMass").floatValue = mass;
            so.FindProperty("drag").floatValue = drag;
            so.ApplyModifiedPropertiesWithoutUndo();
            offers.Add((m, sets));
            built++;
        }

        // ---------------- store assembly ----------------

        // A missile prefab from a stock base: our mesh, the base's effects, an optional seeker transplant, tuned missile fields, and
        // optional tuning of the seeker it ends up with.
        static GameObject Missile(string basePrefab, string id, float length, float radius, string seekerFrom, string seekerType,
            Action<SerializedObject> tune, Action<SerializedObject> tuneNewSeeker = null, string ownSeeker = null, Action<SerializedObject> tuneOwnSeeker = null)
        {
            var go = MakeMissile(basePrefab, $"taiga_{id}", meshes[id], length, radius, seekerFrom, seekerType);
            Tune(go, tune);
            if (seekerType != null && tuneNewSeeker != null) TuneSeeker(go, seekerType, tuneNewSeeker);
            if (ownSeeker != null && tuneOwnSeeker != null) TuneSeeker(go, ownSeeker, tuneOwnSeeker);
            go.name = id;
            return SavePrefab(go, $"{Dir}/taiga_{id}.prefab");
        }

        // The definition, the weapon info and (with a launcher) the mount for a missile prefab; the mount is offered on the given sets.
        // Returns the info.
        static ScriptableObject Store(GameObject prefab, string defBase, string name, string defDesc, string infoBase, string infoDesc,
            float minRange, float maxRange, float minAlignment, float cost, float mass, string mountBase, string pylonChild, string missileChild,
            string launcher, float missileY, float levelDeg, float railNoseZ, float mountMass, float emptyMass, float drag, params int[] sets)
        {
            var id = prefab.name.Replace("taiga_", "");
            var key = "taiga_" + id;
            var def = CloneDef(defBase, $"{key}_def", d =>
            {
                d.FindProperty("jsonKey").stringValue = key;
                d.FindProperty("unitName").stringValue = name;
                d.FindProperty("description").stringValue = defDesc;
                d.FindProperty("mass").floatValue = mass;
                d.FindProperty("unitPrefab").objectReferenceValue = prefab;
            });
            SetDefinition(prefab, def);
            var info = CloneInfo(infoBase, $"{key}_info", prefab, name, name, infoDesc, minRange, maxRange, minAlignment, cost, mass);
            built++;
            if (mountBase == null) return info;
            Mount(info, id, mountBase, pylonChild, missileChild, launcher, id, missileY, LengthOf(id), RadiusOf(id), levelDeg, railNoseZ, name, mountMass, emptyMass, drag, sets);
            return info;
        }

        static float LengthOf(string id) => meshes[id].bounds.size.z;
        static float RadiusOf(string id) => Mathf.Min(meshes[id].bounds.size.x, meshes[id].bounds.size.y) / 2f;

        static void Mount(ScriptableObject info, string id, string mountBase, string pylonChild, string missileChild, string launcher, string store,
            float missileY, float length, float radius, float levelDeg, float railNoseZ, string mountName, float mass, float emptyMass, float drag, params int[] sets)
        {
            var prefab = MakeMount(mountBase, $"taiga_{id}_{launcher}", pylonChild, missileChild, meshes[launcher], meshes[store], missileY, info, length,
                Mathf.Min(radius, 0.25f), $"{Dir}/taiga_{id}_{launcher}.prefab", levelDeg, railNoseZ);
            // the mount's key must differ from the store's definition key (taiga_<id>): with both the same, one replaced the other in
            // the encyclopedia and the Kh-25MP never showed in the loadout menu (in-game test, 5 Nov)
            var key = id.EndsWith("_outer") ? $"taiga_{id}" : $"taiga_{id}_single";
            var m = CloneMount(mountBase, $"taiga_{id}_mount", prefab, info, key, mountName, mass, emptyMass, drag);
            offers.Add((m, sets));
        }

        // ---------------- Blueprinter ops: the MiG stations ----------------

        static void Offer()
        {
            int n = 0;
            var perJet = Jets.ToDictionary(j => j.Key, j => 0);
            foreach (var (mount, roles) in offers)
            {
                var key = new SerializedObject(mount).FindProperty("jsonKey").stringValue;
                var op = ScriptableObject.CreateInstance<Blueprinter.OpAddWeaponToHardpoint>();
                op.weaponJsonKey = key;
                foreach (var jet in Jets)
                {
                    if (jet.Skip.Any(s => key.StartsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                    var sets = roles.SelectMany(r => jet.Roles[r]).Distinct().OrderBy(i => i).ToList();
                    if (sets.Count == 0) continue;
                    op.aircraft.Add(new Blueprinter.OpAddWeaponToHardpoint.AircraftTarget { aircraftJsonKey = jet.Key, hardpointIndices = sets });
                    perJet[jet.Key]++;
                }
                AssetDatabase.CreateAsset(op, $"{OpsDir}/OpAddWeaponToHardpoint_{key}.asset");
                n++;
            }
            Debug.Log($"[Taiga] {n} add-weapon ops: " + string.Join(", ", Jets.Select(j => $"{j.Name} {perJet[j.Key]}")));
        }

        // ---------------- helpers (after the MiG-29 mods' MiG29Weapons, same author) ----------------

        static T Save<T>(T obj, string path) where T : UnityEngine.Object
        {
            AssetDatabase.CreateAsset(obj, path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        static Mesh ToMesh(PartDump p)
        {
            int n = p.vertices.Length / 3;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]);
                nr[i] = new Vector3(p.normals[i * 3], p.normals[i * 3 + 1], p.normals[i * 3 + 2]);
                uv[i] = new Vector2(p.uvs[i * 2], p.uvs[i * 2 + 1]);
            }
            var m = new Mesh { name = "taiga_" + p.name, vertices = v, normals = nr, uv = uv, triangles = p.triangles };
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        static Texture2D ImportTex(string file, bool srgb, bool normal)
        {
            var dst = $"{Dir}/{file}";
            File.Copy(Path.Combine(SourceDir, file), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.filterMode = FilterMode.Point;          // flat colour cells in the atlas
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static Material ArsenalMaterial()
        {
            var atlas = ImportTex("arsenal_atlas.png", true, false);
            var m = new Material(AssetDatabase.LoadAssetAtPath<Material>(BaseSkin)) { name = "taiga_arsenal" };
            m.SetTexture("_Basecolor", atlas); m.SetTexture("_Livery", atlas); m.SetTexture("_BasecolorDmg", atlas);
            var nrm = ImportTex("flat_normal.png", false, true);
            m.SetTexture("_Normal", nrm); m.SetTexture("_NormalDmg", nrm);
            m.SetTexture("_Metallic", ImportTex("arsenal_metallic.png", false, false));
            m.SetTexture("_AO", null);
            return m;
        }

        static Transform Child(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t == null) throw new Exception($"[Taiga] {root.name}: child not found: {name}");
            return t;
        }

        static GameObject Instance(string basePrefab, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GO + basePrefab + "_PLACEHOLDER.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            return go;
        }

        static GameObject MakeMissile(string basePrefab, string name, Mesh mesh, float length, float radius, string seekerFrom, string seekerType)
        {
            var go = Instance(basePrefab, name);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;
                if (mf.transform == go.transform) { mf.sharedMesh = mesh; r.sharedMaterials = new[] { mat }; }
                else mf.sharedMesh = null;
            }
            if (go.GetComponent<MeshFilter>() == null || go.GetComponent<MeshFilter>().sharedMesh != mesh)
            {
                // a base whose mesh sits on a child (some bombs): ours goes on the root
                var mf = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { mat };
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLower();
                if (t == go.transform) continue;
                if (n.Contains("fire") || n.Contains("smoke") || n.Contains("effects")) t.localPosition = new Vector3(0, 0, -length / 2 - 0.05f);
                if (n == "missilecam") t.localPosition = new Vector3(0, 0, -length);
            }
            foreach (var c in go.GetComponentsInChildren<CapsuleCollider>(true)) { c.radius = radius; c.height = length; c.center = Vector3.zero; c.direction = 2; }

            if (seekerFrom != null)
            {
                foreach (var s in go.GetComponents(T("MissileSeeker"))) UnityEngine.Object.DestroyImmediate(s, true);
                var donor = AssetDatabase.LoadAssetAtPath<GameObject>(GO + seekerFrom + "_PLACEHOLDER.prefab");
                var donorSeeker = donor.GetComponent(T(SeekerType(seekerType)));                // "*": whichever seeker the donor has
                ComponentUtility.CopyComponent(donorSeeker);
                ComponentUtility.PasteComponentAsNew(go);
                var donorPath = AssetDatabase.GetAssetPath(donor);
                var sso = new SerializedObject(go.GetComponent(donorSeeker.GetType()));
                var it = sso.GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null && AssetDatabase.GetAssetPath(it.objectReferenceValue) == donorPath)
                        it.objectReferenceValue = null;
                sso.FindProperty("missile").objectReferenceValue = go.GetComponent(T("Missile"));
                sso.ApplyModifiedPropertiesWithoutUndo();
                // the seeker donor's mode and warhead arming (a SARH or IR seeker on an active-radar airframe must not spawn armed)
                var mso = new SerializedObject(go.GetComponent(T("Missile")));
                var dso = new SerializedObject(donor.GetComponent(T("Missile")));
                mso.FindProperty("seekerMode").enumValueIndex = dso.FindProperty("seekerMode").enumValueIndex;
                mso.FindProperty("warhead.Armed").boolValue = dso.FindProperty("warhead.Armed").boolValue;
                mso.ApplyModifiedPropertiesWithoutUndo();
            }
            return go;
        }

        static void Tune(GameObject go, Action<SerializedObject> edit)
        {
            if (edit == null) return;
            var so = new SerializedObject(go.GetComponent(T("Missile")));
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void TuneSeeker(GameObject go, string type, Action<SerializedObject> edit)
        {
            var c = go.GetComponent(T(SeekerType(type)));
            if (c == null) throw new Exception($"[Taiga] {go.name}: no {type}");
            var so = new SerializedObject(c);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // a motor stage, when the base has that many (bombs and some bases have none or one)
        static void Motor(SerializedObject so, int i, float thrust, float burn, float fuel, float tvc = 0f)
        {
            var motors = so.FindProperty("motors");
            if (motors == null || i >= motors.arraySize) { Debug.LogWarning($"[Taiga] {so.targetObject.name}: no motor {i}, left as the base's"); return; }
            var m = motors.GetArrayElementAtIndex(i);
            m.FindPropertyRelative("thrust").floatValue = thrust;
            m.FindPropertyRelative("burnTime").floatValue = burn;
            m.FindPropertyRelative("fuelMass").floatValue = fuel;
            var tv = m.FindPropertyRelative("thrustVectoring");
            if (tv != null) tv.floatValue = tvc;
        }

        static string SeekerType(string type) => type == "*" ? "MissileSeeker" : type;

        static GameObject SavePrefab(GameObject go, string path)
        {
            var p = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return p;
        }

        static void SetDefinition(GameObject prefab, ScriptableObject def)
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            var so = new SerializedObject(root.GetComponent(T("Missile")));
            so.FindProperty("definition").objectReferenceValue = def;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static ScriptableObject Clone(string src, string dstName)
        {
            var source = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MB + src + "_PLACEHOLDER.asset");
            if (source == null) throw new Exception($"[Taiga] stock asset not found: {src}");
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = dstName;
            // Some stock stores are switched off (the ARM2 and the 400 kg cluster bomb): the copy would stay out of the loadout menu.
            var so = new SerializedObject(copy);
            var off = so.FindProperty("disabled");
            if (off != null) { off.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            return Save(copy, $"{Dir}/{dstName}.asset");
        }

        static ScriptableObject CloneDef(string src, string name, Action<SerializedObject> edit)
        {
            var d = Clone(src, name);
            var so = new SerializedObject(d); edit(so); so.ApplyModifiedPropertiesWithoutUndo();
            return d;
        }

        static ScriptableObject CloneInfo(string src, string name, GameObject prefab, string weaponName, string shortName, string desc,
            float minRange, float maxRange, float minAlignment, float cost, float massPerRound)
        {
            var info = Clone(src, name);
            var so = new SerializedObject(info);
            so.FindProperty("weaponPrefab").objectReferenceValue = prefab;
            so.FindProperty("weaponName").stringValue = weaponName;
            so.FindProperty("shortName").stringValue = shortName;
            so.FindProperty("description").stringValue = desc;
            so.FindProperty("targetRequirements.minRange").floatValue = minRange;
            so.FindProperty("targetRequirements.maxRange").floatValue = maxRange;
            so.FindProperty("targetRequirements.minAlignment").floatValue = minAlignment;
            so.FindProperty("costPerRound").floatValue = cost;
            so.FindProperty("massPerRound").floatValue = massPerRound;
            so.ApplyModifiedPropertiesWithoutUndo();
            return info;
        }

        static void SetLaserGuided(ScriptableObject info)
        {
            var so = new SerializedObject(info);
            so.FindProperty("laserGuided").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject MakeMount(string basePrefab, string name, string pylonChild, string missileChild, Mesh launcher, Mesh missile,
            float missileY, ScriptableObject info, float length, float radius, string path, float levelDeg, float railNoseZ)
        {
            var go = Instance(basePrefab, name);
            var rootMf = go.GetComponent<MeshFilter>();
            if (rootMf != null) rootMf.sharedMesh = null;
            var pylon = Child(go.transform, pylonChild);
            var msl = Child(go.transform, missileChild);
            // the base's other decorative children (fins, casings, glide wings) go: only our launcher and store are drawn
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.transform != pylon && mf.transform != msl && mf.transform != go.transform) mf.sharedMesh = null;

            pylon.localPosition = Vector3.zero; pylon.localRotation = Quaternion.identity; pylon.localScale = Vector3.one;
            pylon.GetComponent<MeshFilter>().sharedMesh = launcher;
            pylon.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
            foreach (var bc in pylon.GetComponents<BoxCollider>())
            {
                var b = launcher.bounds; var size = b.size; size.y = Mathf.Max(size.y - 0.03f, 0.02f);
                bc.center = b.center + Vector3.up * (b.size.y - size.y) / 2; bc.size = size;
            }

            float drop = -railNoseZ * Mathf.Tan(levelDeg * Mathf.Deg2Rad);
            msl.localPosition = new Vector3(0, missileY - 0.015f + drop, 0); msl.localRotation = Quaternion.Euler(-levelDeg, 0f, 0f); msl.localScale = Vector3.one;
            msl.GetComponent<MeshFilter>().sharedMesh = missile;
            msl.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
            foreach (var c in msl.GetComponents<CapsuleCollider>()) { c.radius = radius; c.height = length; c.center = Vector3.zero; c.direction = 2; }
            var lod = msl.GetComponent<LODGroup>();
            if (lod != null) lod.RecalculateBounds();

            var so = new SerializedObject(msl.GetComponent(T("MountedMissile")));
            so.FindProperty("info").objectReferenceValue = info;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(go, path);
        }

        static ScriptableObject CloneMount(string src, string name, GameObject prefab, ScriptableObject info,
            string jsonKey, string mountName, float mass, float emptyMass, float drag)
        {
            var m = Clone(src, name);
            var so = new SerializedObject(m);
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("info").objectReferenceValue = info;
            so.FindProperty("jsonKey").stringValue = jsonKey;
            so.FindProperty("mountName").stringValue = mountName;
            so.FindProperty("mass").floatValue = mass;
            so.FindProperty("emptyMass").floatValue = emptyMass;
            so.FindProperty("drag").floatValue = drag;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }
    }
}
