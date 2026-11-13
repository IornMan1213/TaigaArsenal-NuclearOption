# Weapons

Every store in Taiga Arsenal, with the stations it can hang on. The **Stations** column uses station roles; the table at the
bottom says which stations each supported aircraft gives to each role.

## GSh-30-1 cannon rounds

Pick one on the cannon station of the MiG-29s or the Su-30VXN (150 rounds, about 1,650 rounds per minute).

| Round | Type | Use |
|---|---|---|
| OFZ | High-explosive incendiary | the most damage against aircraft and soft targets; no tracers |
| OFZT | High-explosive tracer | the OFZ with a tracer in every round, for walking fire onto a target |
| BR-T | Armour-piercing tracer | solid steel shot for armour; no filler |
| BZT | Armour-piercing incendiary tracer | armour, then the fuel behind it |
| RTP | Practice tracer | inert training round with service ballistics |
| ME | Semi-armour-piercing HE incendiary | bursts inside the target after piercing its skin |
| MKETF | Programmable airburst | bursts beside the target: drones and small aircraft |
| OFZT-SR | High-explosive tracer, self-destructing | the OFZT that bursts in the air after about 2.5 km |

## Air-to-air missiles

| Store | Guidance | Stations |
|---|---|---|
| R-27ER | semi-active radar, extended range | inner, middle |
| R-27ET | infrared, extended range | inner, middle |
| R-74M | infrared, thrust vectoring | inner, middle, outer |
| R-77 | active radar, fire-and-forget | inner, middle |
| R-24R | semi-active radar (early prototypes) | inner, middle |
| R-24T | infrared (early prototypes) | inner, middle |

## Air-to-surface missiles

| Store | Guidance | Stations |
|---|---|---|
| Kh-25ML | laser | inner, middle |
| Kh-25MP | anti-radiation | inner, middle |
| Kh-38ML | laser | inner, middle |
| Kh-38MT | thermal imaging, fire-and-forget | inner, middle |
| Kh-59M | stand-off cruise missile | inner |
| Kh-35U | sea-skimming anti-ship | inner |
| Kh-29L | heavy, laser | inner |
| Kh-29T | heavy, TV, fire-and-forget | inner |
| Kh-29TD | heavy, TV, longer range | inner |
| Kh-38MK | satellite, fire-and-forget | inner, middle |
| Kh-31A | supersonic ramjet anti-ship | inner |
| Kh-31P | supersonic ramjet anti-radiation | inner |
| Kh-59 | TV-guided stand-off missile | inner |

## Bombs

| Store | Type | Stations |
|---|---|---|
| KAB-500S-E | 500 kg satellite-guided | inner, centreline |
| KAB-250LG-E | 250 kg laser-guided | inner, middle, centreline |
| FAB-250 M-62 | 250 kg free-fall | inner, middle, centreline |
| FAB-500 M-62 | 500 kg free-fall | inner, centreline |
| OFAB-250-270 | 250 kg fragmentation | inner, middle, centreline |
| ODAB-500PM | 500 kg fuel-air explosive | inner, centreline |
| BetAB-500ShP | concrete-piercing | inner, centreline |
| RBK-500 | cluster bomb | inner, centreline |
| KAB-500L | 500 kg laser-guided | inner, centreline |
| KAB-500Kr | 500 kg TV-guided | inner, centreline |
| KAB-250S-E | 250 kg satellite-guided | inner, middle, centreline |
| PBK-500U Drel | guided gliding cluster bomb | inner, centreline |
| FAB-100 | 100 kg free-fall | inner, middle, centreline |
| ZAB-500 | 500 kg incendiary | inner, centreline |
| RBK-250 | 250 kg cluster bomb | inner, middle, centreline |
| KMGU-2 | bomblet dispenser | inner, centreline |
| RN-40 | tactical nuclear bomb, 1.5 kt | centreline |

## Rockets

| Store | Rockets | Stations |
|---|---|---|
| B-8M1 pod | 20 x S-8KOM, 80 mm | inner, middle |
| B-13L pod | 5 x S-13OF, 122 mm | inner, middle |
| S-24B | 1 x 240 mm on an APU-68 rail | inner, middle |
| UB-32 pod | 32 x S-5M, 57 mm | inner, middle |
| S-25OF | 1 x 340 mm in its O-25 tube | inner |

## Pods

| Store | Use | Stations |
|---|---|---|
| MSP-418K | radar jamming (the game's ECM pod) | middle, outer |

## How the stores work

Each store is one of the game's own weapons, cloned for its effects, networking and guidance, with its own model
(`tools/arsenal_gen.py`) and retuned to the real weapon. Where the game has no matching behaviour:

- **Free-fall bombs.** Every bomb in the game has a seeker that corrects onto targets near its aim point. The FAB, OFAB, ODAB, BetAB
  and RBK keep it with a 3 m search radius: they fall ballistically and only trim at the very end.
- **Unguided rockets.** The game's rockets are laser-guided. The S-8, S-13 and S-24 have their seeker cone closed to half a degree,
  so they fly where they are pointed.
- **Incendiary rounds.** The game has no fire damage for gun rounds: the incendiary effect is a little extra blast. The ZAB-500
  incendiary bomb is a wide, weaker blast for the same reason.
- **Airburst and self-destruct rounds.** The MKETF uses the proximity fuze of the game's anti-aircraft gun rounds; the OFZT-SR
  bursts after 3 seconds of flight.
- **KMGU-2.** The real dispenser stays on the jet; the game has no such store, so the container is released and opens at once.
- **Not included.** The T220/E targeting pod (the game has no targeting pod to base one on) and the internal SPS-141 / Gardeniya
  jammers (built into the airframe, not stores).

## Stations on each aircraft

| Role | MiG-29 Fulcrum / MiG-29SMT | Su-30VXN Gandiva |
|---|---|---|
| cannon | GSh-30-1 | Internal Cannon |
| centreline | Centreline | Front and Rear Centerline Pylons |
| inner | Inner Pylons | Fuselage (intake) Pylons, Inner and Middle Wing Pylons |
| middle | Middle Pylons | Middle and Outer Wing Pylons |
| outer | Outer Pylons | Wingtip Pylons |

The game does not check whether stores on neighbouring stations overlap, so avoid pairing large stores on adjacent pylons.
