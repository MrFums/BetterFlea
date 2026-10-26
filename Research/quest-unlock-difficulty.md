# Quest unlock difficulty

165 quests unlock 231 trader items. Very hard 19, hard 25, medium 66, easy 55.

Every quest that unlocks a trader item, scored by how hard it is to get to and finish. Written by
`Research/tools/quest_difficulty.py` from the clean install's SPT database. For gun pricing: a part a hard
quest unlocks should keep more of its flea price.

**How it's scored** (0 to 1):
- **Level, 35%:** the highest level lock anywhere in the quest's chain. Level 45+ counts in full.
- **Chain, 45%:** the work in this quest plus every quest before it, on a log scale (150 points counts in full).
- **This quest, 20%:** its own work. 12 points counts in full.
- A level lock is worth at least level / 75 by itself (level 45 = 0.6).
- Anything in the Lightkeeper chain is at least 0.9.
- Seasonal event quests are at least 0.8. Non-seasonal event quests are 1.0: SPT never gives them
  (`showNonSeasonalEventQuests` is off), so what they unlock can't be bought from a trader at all.

Work points: PMC kill 0.25, scav 0.04, boss 3 each (Goons 4, Zryachiy 5, followers 1.5, raiders/rogues 1),
times 2 for headshots only, 100 m+ or all in one raid, 1.3 for a set weapon or target gear, 1.2 for time
of day. Map: Labs/Labyrinth 3, Lighthouse/Reserve/Streets 2, Shoreline/Interchange/night Factory 1.
Hand-ins: roubles / 100k found in raid (/300k otherwise), at most 5. Skill 0.4 per level, loyalty
requirement 1 per level over 1, Lightkeeper's own quests +2. Needing trader LL3/LL4 to start the chain: 4/6.

Tiers: very hard 0.75+, hard 0.55+, medium 0.35+, easy below.

| Score | Tier | Quest | Trader | Level | Depth | Quests before | Chain pts | Own pts | Why | Unlocks |
|---|---|---|---|---|---|---|---|---|---|---|
| 1.00 | very hard | A Key to Salvation | Therapist | 10 | 1 | 1 | 20 | 19.0 | event (never given), hand in 1 TerraGroup Labs keycard (Black), hand in 1 TerraGroup Labs keycard (Blue), hand in 1 TerraGroup Labs keycard  | 26x75mm flare cartridge (Acid Green) |
| 1.00 | very hard | Hint | Skier | 0 | 0 | 0 | 37 | 37.3 | event (never given), kill bossBully x1, kill bossGluhar x1, kill bossKilla x1, kill bossKojaniy x1, kill bossKnight x1, kill followerBigPipe | 9x19mm PBP gzh |
| 1.00 | very hard | Cocktail Tasting | Skier | 0 | 0 | 0 | 6 | 6.0 | event (never given), 20 PMC kills | Burris FullField TAC30 1-4x24 30mm riflescope, EOTech Vudu 1-6x24 30mm riflescope, Vortex Razor HD Gen.2 1-6x24 30mm riflescope |
| 1.00 | very hard | The Punisher. Harvest | Peacekeeper | 0 | 0 | 0 | 13 | 13.4 | event (never given), 50 PMC kills | 4.6x30mm AP SX, 5.45x39mm 7N40, 5.56x45mm SSA AP, 7.62x39mm MAI AP, 7.62x51mm M993, 9x19mm PBP gzh |
| 1.00 | very hard | Failed Setup | Mechanic | 0 | 0 | 0 | 16 | 15.9 | event (never given), kill sectantPriest/sectantWarrior x30 | 40mm VOG-25 grenade, 40x46mm M386 (HE) grenade, GP-25 Kostyor 40mm underbarrel grenade launcher, M203 40mm underbarrel grenade launcher |
| 1.00 | very hard | Bloodhounds | Jaeger | 0 | 0 | 0 | 4 | 3.8 | event (never given), kill arenaFighterEvent x30 | 7.62x39mm BP gzh |
| 0.91 | very hard | Trouble in the Big City | Lightkeeper | 33 | 36 | 109 | 256 | 17.1 | Lightkeeper quest, 50 PMC kills, Streets | 6.8x51mm SIG Hybrid, AR-10 7.62x51 Lancer L7AWM 25-round magazine, Advanced current converter, SIG MCX-SPEAR 6.8x51 assault rifle |
| 0.90 | very hard | Payback | Lightkeeper | 33 | 31 | 104 | 210 | 11.7 | Lightkeeper quest, kill bossGluhar x1, kill followerGluharAssault/followerGluharScout/followerGluharSecu x6, Reserve | DevTac Ronin Respirator |
| 0.90 | very hard | Missing Informant | Lightkeeper | 33 | 28 | 101 | 188 | 4.0 | Lightkeeper quest, Streets | Microcontroller board |
| 0.90 | very hard | Following the Bread Crumbs | Lightkeeper | 33 | 33 | 106 | 226 | 8.0 | Lightkeeper quest, kill pmcBot x20 | Far-forward GPS Signal Amplifier Unit |
| 0.90 | very hard | Snatch | Lightkeeper | 33 | 29 | 102 | 192 | 4.6 | Lightkeeper quest, Lighthouse | Atomic Defense CQCM up armored ballistic mask (Black) |
| 0.89 | very hard | Special Equipment | Peacekeeper | 60 | 12 | 20 | 43 | 21.8 | hand in 5 VPX Flash Storage Module FIR, hand in 5 UHF RFID Reader FIR, hand in 5 Virtex programmable processor FIR, hand in 5 Military COFDM | T H I C C item case |
| 0.84 | very hard | Connections Up North | Skier | 33 | 12 | 31 | 67 | 16.2 | 50 PMC kills, set weapon | Sako TRG M10 .338 LM bolt-action sniper rifle |
| 0.83 | very hard | Calibration | Mechanic | 47 | 11 | 19 | 32 | 10.0 | 20 PMC kills, 100m+ | 5.45x39mm 7N40, Altyn bulletproof helmet (Olive Drab) |
| 0.77 | very hard | Hunting Trip | Jaeger | 33 | 11 | 21 | 34 | 11.7 | kill bossKojaniy x1, headshots, 75m+, set weapon | Accuracy International AXMC .338 LM bolt-action sniper rifle |
| 0.77 | very hard | Perfect Mediator | Prapor | 35 | 9 | 9 | 26 | 21.0 | Ragman LL4, Skier LL4, Mechanic LL4, Peacekeeper LL4, Prapor LL4, Therapist LL4, Jaeger LL4 | SR-3M 9x39 compact assault rifle |
| 0.76 | very hard | The Guide | Peacekeeper | 40 | 22 | 22 | 44 | 6.5 | Labs/Lighthouse/Reserve/Streets | AR-15 Vltor MUR-1S 5.56x45 upper receiver, EOTech HHS-1 hybrid sight (Tan), Ops-Core SLAAP armor helmet plate (Tan) |
| 0.76 | very hard | Slaughterhouse | Jaeger | 40 | 12 | 21 | 42 | 6.7 | 10 scav kills, set weapon, Lighthouse/Reserve/Streets | SOG Voodoo Hawk tactical tomahawk |
| 0.75 | very hard | Capturing Outposts | Prapor | 42 | 15 | 15 | 36 | 6.0 | 8 PMC kills | 9x39mm SP-6 gs |
| 0.71 | hard | Psycho Sniper | Mechanic | 35 | 22 | 22 | 43 | 6.1 | Sniper skill 10, 5 PMC kills, set weapon | Kalashnikov AK-104 7.62x39 assault rifle |
| 0.70 | hard | Worst Job in the World | Peacekeeper | 25 | 21 | 23 | 50 | 9.4 | 30 kills, 100m+, set weapon | 40x46mm M386 (HE) grenade, M203 40mm underbarrel grenade launcher |
| 0.69 | hard | Insomnia | Peacekeeper | 25 | 20 | 20 | 43 | 9.5 | 25 PMC kills, time of day, Lighthouse/Reserve/Streets | SAPI level III+ ballistic plate |
| 0.69 | hard | Best Job in the World | Prapor | 25 | 15 | 16 | 42 | 9.4 | 30 kills, 100m+, set weapon | 40mm VOG-25 grenade, GP-25 Kostyor 40mm underbarrel grenade launcher |
| 0.66 | hard | Our Own Land | Prapor | 30 | 14 | 14 | 29 | 7.1 | 20 PMC kills, Lighthouse | 23x75mm Zvezda flashbang round |
| 0.65 | hard | The Huntsman Path - Big Game | Jaeger | 17 | 11 | 22 | 35 | 13.3 | kill bossBoar x1, kill bossBoarSniper/followerBoar x5, kill followerBoarClose1 x1, kill followerBoarClose2 x1, Streets | Armasight Zeus-Pro 640 2-8x50 30Hz thermal scope |
| 0.65 | hard | The Price of Independence | BTR Driver | 20 | 8 | 12 | 29 | 11.5 | in one raid, 3 kills, one raid, 10 scav kills, Lighthouse/Reserve | Graphics card, RSP-30 reactive signal cartridge (Blue) |
| 0.65 | hard | Counteraction | Peacekeeper | 30 | 12 | 12 | 24 | 7.7 | 20 PMC kills, Lighthouse | 23x75mm Zvezda flashbang round |
| 0.65 | hard | Hunter | Jaeger | 13 | 11 | 21 | 46 | 18.1 | Jaeger LL4 to start, kill bossKojaniy x20 | SWORD International Mk-18 .338 LM marksman rifle |
| 0.64 | hard | Peacekeeping Mission | Peacekeeper | 30 | 6 | 6 | 17 | 8.7 | 12 scav kills, set weapon, set gear, Streets | Cult Locust ballistic plate |
| 0.63 | hard | The Huntsman Path - Sellout | Jaeger | 30 | 11 | 23 | 37 | 4.1 | kill bossKilla x1 | RPK-16 5.45x39 light machine gun |
| 0.62 | hard | Gendarmerie - District Patrol | Prapor | 21 | 17 | 17 | 47 | 6.7 | 30 kills, set weapon, Streets | Kalashnikov AK-12 5.45x39 assault rifle |
| 0.62 | hard | A Shooter Born in Heaven | Mechanic | 14 | 5 | 5 | 30 | 28.0 | 5 PMC kills, headshots, set weapon, Lighthouse/Reserve/Streets | HK G28 7.62x51 marksman rifle, Magazine case |
| 0.62 | hard | Test Drive - Part 2 | Prapor | 30 | 2 | 2 | 14 | 8.5 | 20 PMC kills, set weapon, Streets | 9x21mm BT gzh |
| 0.60 | hard | The Punisher - Part 6 | Prapor | 21 | 14 | 14 | 30 | 7.9 | 15 PMC kills, set weapon, Labs/Lighthouse/Reserve/Streets | 7.62x54mm R PS gzh |
| 0.60 | hard | Gendarmerie - Tickets, Please | Prapor | 21 | 16 | 16 | 40 | 5.9 | 25 kills, set weapon, Streets | SR-2M Veresk 9x21 submachine gun |
| 0.59 | hard | The Huntsman Path - Eraser - Part 2 | Jaeger | 30 | 10 | 17 | 30 | 2.9 | kill pmcBot x6 | MP-155 Ultima thermal camera |
| 0.59 | hard | Supervisor | Ragman | 40 | 7 | 12 | 21 | 0.1 |  | FORT Redut-T5 body armor (Smog) |
| 0.59 | hard | Hobby Club | Mechanic | 44 | 1 | 1 | 1 | 1.2 |  | AK-50 .50 BMG 24 inch barrel, AK-50 .50 BMG muzzle brake, AK-50 M-LOK handguard with gas tube, AK-50 dust cover |
| 0.59 | hard | Dangerous Props | Skier | 35 | 9 | 9 | 18 | 3.1 | 20 kills, set weapon | MPS Auto Assault-12 Gen 2 12ga automatic shotgun |
| 0.58 | hard | TerraGroup Employee | Peacekeeper | 23 | 13 | 29 | 48 | 3.0 | Labs | Mesa Tactical Crosshair Hydraulic buffer tube |
| 0.58 | hard | Living High is Not a Crime - Part 1 | Ragman | 27 | 7 | 9 | 21 | 5.5 | hand in 2 Bronze lion figurine FIR | IOTV Gen4 body armor (High Mobility Kit, MultiCam) |
| 0.56 | hard | Scavenger | Ragman | 29 | 6 | 9 | 21 | 3.6 | Search skill 9 | Crash Axe |
| 0.56 | hard | Gendarmerie - Mall Cop | Prapor | 21 | 15 | 15 | 34 | 4.3 | 15 kills, set weapon, Streets | Serdyukov SR-1MP Gyurza 9x21 pistol |
| 0.55 | hard | Sales Night | Ragman | 30 | 5 | 8 | 19 | 3.1 | survive 7 raids | Granit Br4 ballistic plate, HighCom Striker ULACH IIIA helmet (Desert Tan) |
| 0.55 | medium | Gunsmith - Part 25 | Mechanic | 39 | 24 | 25 | 13 | 0.5 |  | Kalashnikov PKM 7.62x54R machine gun, TerraGroup Labs keycard (Black) |
| 0.55 | medium | Flint | Skier | 35 | 7 | 7 | 14 | 2.0 | StressResistance skill 5 | Miller Bros. Blades M-2 Tactical Sword |
| 0.55 | medium | The Art of Explosion | Prapor | 33 | 1 | 1 | 8 | 5.5 | 35 kills, set weapon | RShG-2 72.5mm rocket launcher |
| 0.55 | medium | Gunsmith - Part 24 | Mechanic | 39 | 23 | 24 | 12 | 0.5 |  | Stark SE-5 Express Forward foregrip, Stark SE-5 Express Forward foregrip (FDE) |
| 0.54 | medium | Gunsmith - Part 23 | Mechanic | 39 | 22 | 23 | 12 | 0.5 |  | Mk47 409mm barrel |
| 0.54 | medium | Broadcast - Part 4 | Jaeger | 27 | 7 | 7 | 17 | 4.3 | in one raid, Streets | .45 ACP RIP |
| 0.54 | medium | Inventory Check | Ragman | 15 | 4 | 5 | 23 | 8.3 | in one raid, Reserve | Camelbak Tri-Zip assault backpack (Foliage), Camelbak Tri-Zip assault backpack (MultiCam) |
| 0.54 | medium | Gunsmith - Part 22 | Mechanic | 39 | 21 | 22 | 12 | 0.5 |  | SilencerCo Saker ASR 556 5.56x45 sound suppressor, Trijicon REAP-IR thermal scope |
| 0.54 | medium | Charisma Brings Success | Ragman | 25 | 8 | 10 | 20 | 4.0 | Charisma skill 10 | FORT Redut-M body armor |
| 0.54 | medium | Gunsmith - Part 21 | Mechanic | 38 | 20 | 21 | 11 | 1.0 |  | AR-15 HK Ergo PSG-1 style pistol grip, AR-15 Strike Industries Advanced Receiver Extension buffer tube |
| 0.54 | medium | Athlete | Therapist | 30 | 10 | 10 | 13 | 4.0 | Health skill 10 | Adrenaline injector |
| 0.53 | medium | Irresistible | Skier | 35 | 8 | 8 | 15 | 1.0 |  | MPS Auto Assault-12 Gen 1 12ga automatic shotgun |
| 0.52 | medium | Textile - Part 1 | Ragman | 25 | 8 | 9 | 23 | 2.7 | Ragman LL4 to start, hand in 10 Ripstop fabric FIR | IOTV Gen4 body armor (Assault Kit, MultiCam) |
| 0.52 | medium | Hot Delivery | Ragman | 29 | 5 | 8 | 18 | 1.9 |  | Mystery Ranch Blackjack 50 backpack (MultiCam) |
| 0.52 | medium | Import | Mechanic | 35 | 6 | 6 | 10 | 2.0 | hand in 1 VPX Flash Storage Module FIR | 4.6x30mm FMJ SX |
| 0.52 | medium | Broadcast - Part 5 | Jaeger | 27 | 8 | 8 | 19 | 2.3 | kill sectantPriest x1 | Armasight Zeus-Pro 640 2-8x50 30Hz thermal scope |
| 0.51 | medium | The Blood of War - Part 3 | Ragman | 30 | 6 | 8 | 18 | 0.9 |  | HighCom Trooper TFO body armor (MultiCam) |
| 0.51 | medium | Reconnaissance | Prapor | 18 | 12 | 12 | 19 | 6.3 | in one raid, Lighthouse | Korund-VM ballistic plates (Front) |
| 0.51 | medium | Regulated Materials | Prapor | 25 | 7 | 7 | 9 | 6.6 | hand in 1 6-STEN-140-M military battery FIR, hand in 5 OFZ 30x165mm shell FIR | ASh-12 12.7x55 assault rifle |
| 0.51 | medium | Gunsmith - Part 20 | Mechanic | 37 | 19 | 20 | 10 | 0.5 |  | AR-10 KAC QDC 7.62x51 Muzzle Brake Kit, Magpul MOE Carbine rubber buttpad |
| 0.51 | medium | Audiophile | Ragman | 25 | 5 | 8 | 22 | 2.0 | Streets | CQC Osprey MK4A plate carrier (Assault, MTP) |
| 0.51 | medium | Chemistry Closet | Mechanic | 22 | 10 | 18 | 22 | 3.3 | in one raid | T H I C C Weapon case |
| 0.50 | medium | Gunsmith - Part 19 | Mechanic | 36 | 18 | 19 | 10 | 0.5 |  | AK Custom Arms AGS-74 PRO + Sniper Kit pistol grip |
| 0.50 | medium | Lost Contact | Therapist | 26 | 8 | 8 | 11 | 4.3 | in one raid, Lighthouse | Grizzly medical kit |
| 0.48 | medium | Wet Job - Part 6 | Peacekeeper | 14 | 21 | 21 | 37 | 2.8 | Sniper skill 7 | FN40GL Mk2 40mm grenade launcher |
| 0.48 | medium | Wet Job - Part 3 | Peacekeeper | 14 | 18 | 18 | 32 | 3.3 | in one raid | HK 416A5 5.56x45 assault rifle |
| 0.47 | medium | Easy Job - Part 2 | Prapor | 18 | 12 | 12 | 17 | 4.4 | 20 kills, Lighthouse | AK 7.62x39 Molot Arms 75-round drum magazine |
| 0.47 | medium | Gunsmith - Part 18 | Mechanic | 33 | 17 | 18 | 9 | 0.5 |  | AKM 7.62x39 Kiba Arms .308 muzzle device adapter, Steiner LAS/TAC 2 tactical flashlight |
| 0.47 | medium | Energy Crisis | Mechanic | 25 | 6 | 6 | 11 | 3.2 | Lighthouse | FN SCAR-L 5.56x45 assault rifle (FDE), Granit 4 ballistic plate (Front), Tromix Monster Claw 12ga muzzle brake |
| 0.47 | medium | Samples | Peacekeeper | 21 | 10 | 15 | 16 | 3.2 |  | Ops-Core FAST multi-hit ballistic face shield, Team Wendy EXFIL Ballistic Helmet (Black) |
| 0.47 | medium | Fertilizers | Mechanic | 30 | 6 | 6 | 9 | 1.5 |  | HK USP .45 ACP pistol |
| 0.47 | medium | Missing Cargo | Skier | 30 | 5 | 5 | 8 | 2.0 | Lighthouse | AK Zenit RK-3 pistol grip, AR-15 Magpul MOE Carbine stock (Black), Magpul MOE Carbine rubber buttpad |
| 0.46 | medium | Minibus | Ragman | 24 | 4 | 5 | 14 | 1.9 |  | Interceptor OTV body armor (UCP) |
| 0.46 | medium | Broadcast - Part 3 | Jaeger | 20 | 6 | 6 | 13 | 4.3 | in one raid, Streets | Surv12 field surgical kit |
| 0.46 | medium | The Huntsman Path - Woods Keeper | Jaeger | 13 | 10 | 20 | 22 | 4.5 | kill bossKojaniy x1, hand in 1 Shturman's stash key FIR | Remington Model 700 7.62x51 bolt-action sniper rifle |
| 0.45 | medium | Bullshit | Skier | 25 | 7 | 7 | 13 | 1.4 |  | 12/70 RIP ammo pack (5 pcs), CMMG Mk47 Mutant 7.62x39 assault rifle, Kiba Arms Titan ballistic plate |
| 0.45 | medium | Gunsmith - Part 17 | Mechanic | 31 | 16 | 17 | 8 | 0.5 |  | AK CNC Warrior 5.56x45 muzzle device adapter, AKM/AK-74 Hera Arms CQR47 pistol grip/buttstock, AR-15 Hera Arms CQR pistol grip/buttstock, Magpul AFG tactical foregrip (Olive Drab) |
| 0.45 | medium | Cargo X - Part 4 | Peacekeeper | 12 | 19 | 19 | 34 | 2.3 | Lighthouse | Ops-Core FAST MT Super High Cut helmet (Urban Tan) |
| 0.45 | medium | Revision - Streets of Tarkov | Peacekeeper | 17 | 10 | 10 | 19 | 2.9 | Streets | FN SCAR-L 5.56x45 assault rifle (FDE), Galvion Caiman Hybrid helmet (Grey) |
| 0.45 | medium | Wet Job - Part 5 | Peacekeeper | 14 | 20 | 20 | 34 | 1.0 |  | AFAK tactical individual first aid kit, Remington R11 RSASS 7.62x51 marksman rifle |
| 0.44 | medium | Wet Job - Part 4 | Peacekeeper | 14 | 19 | 19 | 33 | 1.0 |  | Desert Tech MDR 7.62x51 assault rifle |
| 0.44 | medium | Glory to CPSU - Part 2 | Prapor | 22 | 11 | 11 | 13 | 2.0 | Streets | Tokarev AVT-40 7.62x54R automatic rifle |
| 0.44 | medium | Gunsmith - Part 16 | Mechanic | 30 | 15 | 16 | 8 | 0.5 |  | Lone Star TX-15 DML 5.56x45 carbine |
| 0.44 | medium | Wet Job - Part 2 | Peacekeeper | 14 | 17 | 17 | 29 | 1.3 |  | AR-15 5.56x45 Magpul PMAG D-60 STANAG 60-round magazine, MSA ACH TC-2001 MICH Series helmet (Olive Drab) |
| 0.43 | medium | Ambulances Again | Therapist | 17 | 8 | 16 | 18 | 2.3 | Streets | Propital regenerative stimulant injector |
| 0.43 | medium | Sew it Good - Part 3 | Ragman | 25 | 6 | 7 | 13 | 0.1 |  | 6Sh118 raid backpack (EMR) |
| 0.43 | medium | Revision - Lighthouse | Peacekeeper | 14 | 10 | 10 | 19 | 3.2 | Lighthouse | 7.62x51mm M80, FN Five-seveN MK2 5.7x28 pistol (FDE) |
| 0.43 | medium | Ambulance | Jaeger | 25 | 5 | 9 | 7 | 3.0 | hand in 1 Portable defibrillator FIR | Surv12 field surgical kit |
| 0.42 | medium | Spa Tour - Part 7 | Peacekeeper | 12 | 15 | 15 | 26 | 1.6 | hand in 4 Morphine injector FIR | Crye Precision AirFrame helmet (Tan) |
| 0.42 | medium | Ballet Lover | Ragman | 20 | 2 | 2 | 7 | 4.3 | in one raid, Streets | 6B5-15 Zh-86 Uley armored rig (Flora) |
| 0.41 | medium | Overpopulation | Peacekeeper | 15 | 10 | 10 | 17 | 2.5 | 12 scav kills, Lighthouse | DS Arms SA58 7.62x51 assault rifle |
| 0.41 | medium | Private Club | Skier | 24 | 7 | 7 | 12 | 0.0 |  | Saiga-12K 12ga automatic shotgun |
| 0.41 | medium | Cargo X - Part 2 | Peacekeeper | 12 | 17 | 17 | 28 | 1.0 |  | KAC QDSS NT4 5.56x45 sound suppressor (Black) |
| 0.41 | medium | Gunsmith - Part 15 | Mechanic | 27 | 14 | 15 | 8 | 0.5 |  | VSS/VAL TOZ 6P29M mount |
| 0.40 | medium | Gunsmith - Part 14 | Mechanic | 27 | 13 | 14 | 7 | 0.5 |  | AR-15 Magpul PRS GEN3 stock (Black) |
| 0.38 | medium | Gunsmith - Part 13 | Mechanic | 25 | 12 | 13 | 6 | 0.5 |  | KAC PRS/QDC 7.62x51 sound suppressor |
| 0.38 | medium | The Blood of War - Part 1 | Ragman | 15 | 2 | 3 | 12 | 1.9 |  | Hazard 4 Takedown sling backpack (MultiCam) |
| 0.38 | medium | Beyond the Red Meat - Part 2 | Skier | 22 | 11 | 11 | 6 | 2.0 | Streets | Zenit RK-1 tactical foregrip on B-25U mount |
| 0.37 | medium | Long Road | Skier | 20 | 4 | 4 | 6 | 2.3 | 8 scav kills, Lighthouse | Lobaev Arms DVL-10 7.62x51 bolt-action sniper rifle |
| 0.37 | medium | Spa Tour - Part 2 | Peacekeeper | 12 | 10 | 10 | 16 | 1.6 |  | M67 hand grenade |
| 0.37 | medium | Database - Part 2 | Ragman | 15 | 3 | 4 | 12 | 1.0 |  | 6B3TM-01 armored rig (Khaki) |
| 0.36 | medium | Humanitarian Supplies | Peacekeeper | 11 | 8 | 8 | 12 | 2.8 | 10 scav kills, set gear | 5.56x45mm Warmageddon, AK-74 Hexagon 5.45x39 sound suppressor |
| 0.36 | medium | The Bunker - Part 1 | Prapor | 10 | 6 | 6 | 8 | 5.3 | in one raid, Reserve | 9x21mm PS gzh |
| 0.36 | medium | Seaside Vacation | Therapist | 17 | 7 | 7 | 8 | 2.0 | Lighthouse | Aluminum splint |
| 0.36 | medium | Database - Part 1 | Ragman | 15 | 2 | 3 | 11 | 1.0 |  | NFM THOR Concealable Reinforced Vest body armor |
| 0.36 | medium | Health Care Privacy - Part 1 | Therapist | 20 | 6 | 6 | 6 | 1.9 |  | IFAK individual first aid kit |
| 0.35 | easy | The Punisher - Part 1 | Prapor | 17 | 9 | 9 | 7 | 1.8 | 15 scav kills, set weapon | 7.62x39mm PS gzh, SV-98 7.62x54R bolt-action sniper rifle |
| 0.35 | easy | Health Care Privacy - Part 3 | Therapist | 20 | 8 | 8 | 7 | 0.3 |  | Morphine injector |
| 0.35 | easy | Gunsmith - Part 11 | Mechanic | 22 | 10 | 11 | 6 | 0.5 |  | Colt M4A1 5.56x45 assault rifle |
| 0.35 | easy | Beyond the Red Meat - Part 1 | Skier | 22 | 10 | 10 | 4 | 2.0 | Streets | AK CSS knurled charging handle |
| 0.34 | easy | Lend-Lease - Part 1 | Skier | 25 | 4 | 4 | 4 | 0.0 |  | ORSIS T-5000M 7.62x51 bolt-action sniper rifle |
| 0.33 | easy | You've Got Mail | Prapor | 15 | 9 | 9 | 7 | 2.0 | Streets | 9x18mm PM SP7 gzh |
| 0.32 | easy | Gunsmith - Part 10 | Mechanic | 20 | 9 | 10 | 5 | 0.5 |  | Ammunition case |
| 0.32 | easy | Gunsmith - Old Friend's Request | Mechanic | 17 | 7 | 8 | 5 | 1.5 |  | Ballistic plate case |
| 0.31 | easy | Eagle Eye | Peacekeeper | 11 | 7 | 7 | 10 | 1.0 |  | SIG MPX 9x19 submachine gun |
| 0.31 | easy | Scrap Metal | Peacekeeper | 10 | 6 | 6 | 9 | 1.9 |  | 5.7x28mm SS197SR, HK MP5 9x19 submachine gun (Navy 3 Round Burst) |
| 0.31 | easy | Gunsmith - Part 9 | Mechanic | 19 | 8 | 9 | 4 | 0.5 |  | 5.7x28mm R37.F, Colt M4A1 5.56x45 assault rifle |
| 0.30 | easy | Corporate Secrets | Mechanic | 17 | 5 | 5 | 4 | 2.0 | Lighthouse | DevTac Ronin ballistic helmet, Kalashnikov AK-104 7.62x39 assault rifle |
| 0.29 | easy | Vitamins - Part 2 | Skier | 22 | 9 | 9 | 2 | 0.4 |  | TDI KRISS Vector Gen.2 9x19 submachine gun |
| 0.29 | easy | Audit | Ragman | 17 | 1 | 1 | 3 | 2.0 | Streets | 6B13 assault armor (Flora) |
| 0.28 | easy | The Tarkov Shooter - Part 8 | Jaeger | 2 | 11 | 11 | 11 | 2.5 | 3 PMC kills, headshots, set weapon | Mosin Rifle 7.62x54R ProMag Archangel OPFOR 10-round magazine, Mosin Rifle ProMag Archangel OPFOR PRS chassis, Vortex Razor HD Gen.2 1-6x24 30mm riflescope |
| 0.28 | easy | Fishing Gear | Peacekeeper | 10 | 4 | 4 | 6 | 1.6 |  | SIG Sauer ROMEO4 reflex sight, Springfield Armory M1A 7.62x51 rifle |
| 0.28 | easy | Tigr Safari | Peacekeeper | 10 | 5 | 5 | 7 | 0.9 |  | 5.56x45mm FMJ, B&T MP9 9x19 submachine gun |
| 0.27 | easy | Bad Habit | Mechanic | 12 | 4 | 4 | 4 | 2.2 |  | Bottle of OLOLO Multivitamins |
| 0.26 | easy | Gunsmith - Part 7 | Mechanic | 15 | 6 | 7 | 4 | 0.5 |  | AR-15 Daniel Defense MK12 Low Profile Gas Block |
| 0.25 | easy | Hot Zone | Ragman | 15 | 5 | 5 | 3 | 0.6 |  | Labrys access keycard |
| 0.25 | easy | Friend From the West - Part 2 | Skier | 9 | 3 | 3 | 4 | 2.0 | hand in 5000 Dollars | TDI KRISS Vector Gen.2 .45 ACP submachine gun |
| 0.25 | easy | The Tarkov Shooter - Part 7 | Jaeger | 2 | 10 | 10 | 9 | 1.6 | 5 PMC kills, set weapon | Mosin Rifle Witt Machine 7.62x54R muzzle brake |
| 0.24 | easy | Search Mission | Prapor | 5 | 2 | 2 | 4 | 3.3 | in one raid | NPP KlASS Kora-Kulon body armor (EMR) |
| 0.24 | easy | Gunsmith - Part 6 | Mechanic | 14 | 5 | 6 | 3 | 0.5 |  | AK GP-25 accessory kit recoil pad |
| 0.24 | easy | Shaking Up the Teller | Prapor | 9 | 8 | 8 | 5 | 0.0 |  | AKS-74U PBS-4 5.45x39 sound suppressor, Hexagon 12K 12ga sound suppressor |
| 0.23 | easy | Postman Pat - Part 2 | Therapist | 10 | 9 | 9 | 5 | 0.0 |  | Salewa first aid kit |
| 0.23 | easy | The Tarkov Shooter - Part 4 | Jaeger | 2 | 7 | 7 | 6 | 2.4 | 5 PMC kills, 80m+, set weapon | Mosin Rifle AIM Sports Tri-Rail mount, Mosin Rifle Tacfire pistol grip |
| 0.22 | easy | The Tarkov Shooter - Part 6 | Jaeger | 2 | 9 | 9 | 7 | 1.0 | kill marksman x5, set weapon | Mosin Rifle ATI Monte Carlo stock |
| 0.22 | easy | The Survivalist Path - Wounded Beast | Jaeger | 2 | 7 | 7 | 8 | 0.6 | 3 scav kills | Benelli M3 Super 90 12ga dual-mode shotgun |
| 0.22 | easy | To Great Heights! - Part 5 [PVE ZONE] | Ref | 3 | 8 | 8 | 8 | 0.0 |  | APOK Tactical Wasteland Gladius |
| 0.22 | easy | Operation Aquarius - Part 2 | Therapist | 6 | 3 | 3 | 5 | 0.6 | 15 scav kills | Army bandage |
| 0.21 | easy | Ice Cream Cones | Prapor | 9 | 7 | 7 | 3 | 0.6 |  | AK-74 5.45x39 6L31 60-round magazine |
| 0.20 | easy | The Tarkov Shooter - Part 5 | Jaeger | 2 | 8 | 8 | 6 | 0.5 | 8 scav kills, set weapon, time of day | Mosin Rifle Texas Precision Products 7.62x54R muzzle brake |
| 0.20 | easy | The Tarkov Butcher | Therapist | 15 | 5 | 5 | 0 | 0.3 |  | Labrys access keycard |
| 0.20 | easy | Gunsmith - Part 5 | Mechanic | 10 | 4 | 5 | 2 | 0.5 |  | Ammunition case |
| 0.18 | easy | Gunsmith - Part 4 | Mechanic | 9 | 3 | 4 | 2 | 0.5 |  | PP-19-01 Vityaz 9x19 submachine gun |
| 0.17 | easy | The Survivalist Path - Thrifty | Jaeger | 2 | 5 | 5 | 4 | 1.2 |  | Iskra ration pack |
| 0.17 | easy | Bad Rep Evidence | Prapor | 6 | 6 | 6 | 3 | 0.3 |  | BelOMO PSO-1M2-1 4x24 scope, SSSh-94 SFERA-S helmet |
| 0.17 | easy | BP Depot | Prapor | 5 | 5 | 5 | 2 | 1.2 |  | PP-9 Klin 9x18PMM submachine gun |
| 0.17 | easy | The Tarkov Shooter - Part 3 | Jaeger | 2 | 6 | 6 | 3 | 1.0 | 3 PMC kills, set weapon | Mosin Rifle AIM Sports Recoil Pad, Mosin Rifle Tacfire Tanker Style 7.62x54R muzzle brake |
| 0.16 | easy | Chemical - Part 2 | Skier | 10 | 6 | 6 | 1 | 0.0 |  | SOK-12 12ga SAI-02 10-round magazine |
| 0.15 | easy | Stirrup | Skier | 8 | 2 | 2 | 1 | 1.0 | 3 PMC kills, set weapon | Chiappa Rhino 50DS .357 revolver |
| 0.15 | easy | Golden Swag | Skier | 8 | 4 | 4 | 1 | 0.3 |  | VOMZ Pilad P1x42 Weaver reflex sight |
| 0.14 | easy | Sanitary Standards - Part 1 | Therapist | 4 | 2 | 2 | 2 | 0.2 |  | Car first aid kit |
| 0.14 | easy | The Tarkov Shooter - Part 2 | Jaeger | 2 | 5 | 5 | 2 | 0.5 | 3 kills, set weapon, 2 kills, headshots | Mosin Rifle AIM Sports MNG rail mount |
| 0.12 | easy | Gunsmith - Part 3 | Mechanic | 7 | 2 | 2 | 1 | 0.5 |  | Glock 17 9x19 pistol |
| 0.12 | easy | Shootout Picnic | Prapor | 3 | 4 | 4 | 1 | 0.6 | 15 scav kills | 6B5-16 Zh-86 Uley armored rig (Khaki), Lebedev PL-15 9x19 pistol |
| 0.11 | easy | Delivery From the Past | Prapor | 5 | 4 | 4 | 1 | 0.3 |  | 5.45x39mm FMJ |
| 0.11 | easy | Gunsmith - Part 2 | Mechanic | 5 | 2 | 2 | 1 | 0.5 |  | AK 100-series polymer handguard |
| 0.10 | easy | Every Hunter Knows This | Jaeger | 2 | 5 | 5 | 2 | 0.0 |  | Tripwire installation kit |
| 0.09 | easy | The Extortionist | Skier | 7 | 2 | 2 | 0 | 0.0 |  | Remington Model 870 12ga pump-action shotgun |
| 0.07 | easy | Debut | Prapor | 1 | 1 | 1 | 1 | 0.3 | 5 scav kills | Kalashnikov AKS-74UB 5.45x39 assault rifle |
| 0.07 | easy | Supplier | Skier | 5 | 1 | 1 | 0 | 0.0 |  | 7.62x54mm R SP BT, Saiga-9 9x19 carbine |
| 0.06 | easy | Gunsmith - Part 1 | Mechanic | 2 | 1 | 1 | 0 | 0.5 |  | AR-15 Windham Weaponry Rail Gas Block |
| 0.00 | easy | Protect the Sky | BTR Driver | 0 | 0 | 0 | 0 | 0.0 |  | Aklys Defense Velociraptor .300 Blackout assault rifle |

## Guns and gun parts

Each gun or part a quest unlocks, by its easiest quest. Ones another trader also sells without a quest are left
out: gun pricing goes by the easiest way to buy a part, so the quest doesn't matter for them.

| Score | Tier | Item | Quest | Trader | LL |
|---|---|---|---|---|---|
| 0.91 | very hard | AR-10 7.62x51 Lancer L7AWM 25-round magazine | Trouble in the Big City | Peacekeeper | 4 |
| 0.84 | very hard | Sako TRG M10 .338 LM bolt-action sniper rifle | Connections Up North | Skier | 4 |
| 0.77 | very hard | Accuracy International AXMC .338 LM bolt-action sniper rifle | Hunting Trip | Jaeger | 4 |
| 0.76 | very hard | EOTech HHS-1 hybrid sight (Tan) | The Guide | Peacekeeper | 3 |
| 0.76 | very hard | AR-15 Vltor MUR-1S 5.56x45 upper receiver | The Guide | Peacekeeper | 4 |
| 0.70 | hard | M203 40mm underbarrel grenade launcher | Worst Job in the World | Peacekeeper | 3 |
| 0.69 | hard | GP-25 Kostyor 40mm underbarrel grenade launcher | Best Job in the World | Prapor | 3 |
| 0.65 | hard | RSP-30 reactive signal cartridge (Blue) | The Price of Independence | Mechanic | 2 |
| 0.59 | hard | MP-155 Ultima thermal camera | The Huntsman Path - Eraser - Part 2 | Jaeger | 4 |
| 0.59 | hard | AK-50 .50 BMG muzzle brake | Hobby Club | Mechanic | 4 |
| 0.59 | hard | AK-50 M-LOK handguard with gas tube | Hobby Club | Mechanic | 4 |
| 0.59 | hard | AK-50 dust cover | Hobby Club | Mechanic | 4 |
| 0.59 | hard | AK-50 .50 BMG 24 inch barrel | Hobby Club | Mechanic | 4 |
| 0.59 | hard | MPS Auto Assault-12 Gen 2 12ga automatic shotgun | Dangerous Props | Skier | 4 |
| 0.58 | hard | Mesa Tactical Crosshair Hydraulic buffer tube | TerraGroup Employee | Peacekeeper | 4 |
| 0.55 | medium | Kalashnikov PKM 7.62x54R machine gun | Gunsmith - Part 25 | Mechanic | 4 |
| 0.55 | medium | RShG-2 72.5mm rocket launcher | The Art of Explosion | Prapor | 4 |
| 0.54 | medium | Mk47 409mm barrel | Gunsmith - Part 23 | Mechanic | 4 |
| 0.54 | medium | SilencerCo Saker ASR 556 5.56x45 sound suppressor | Gunsmith - Part 22 | Peacekeeper | 4 |
| 0.54 | medium | AR-15 HK Ergo PSG-1 style pistol grip | Gunsmith - Part 21 | Mechanic | 4 |
| 0.54 | medium | AR-15 Strike Industries Advanced Receiver Extension buffer tube | Gunsmith - Part 21 | Mechanic | 4 |
| 0.53 | medium | MPS Auto Assault-12 Gen 1 12ga automatic shotgun | Irresistible | Skier | 4 |
| 0.52 | medium | Armasight Zeus-Pro 640 2-8x50 30Hz thermal scope | Broadcast - Part 5 | Jaeger | 4 |
| 0.51 | medium | AR-10 KAC QDC 7.62x51 Muzzle Brake Kit | Gunsmith - Part 20 | Mechanic | 3 |
| 0.50 | medium | AK Custom Arms AGS-74 PRO + Sniper Kit pistol grip | Gunsmith - Part 19 | Skier | 4 |
| 0.47 | medium | AK 7.62x39 Molot Arms 75-round drum magazine | Easy Job - Part 2 | Prapor | 3 |
| 0.47 | medium | AKM 7.62x39 Kiba Arms .308 muzzle device adapter | Gunsmith - Part 18 | Mechanic | 3 |
| 0.47 | medium | Tromix Monster Claw 12ga muzzle brake | Energy Crisis | Mechanic | 3 |
| 0.47 | medium | AR-15 Magpul MOE Carbine stock (Black) | Missing Cargo | Skier | 3 |
| 0.47 | medium | AK Zenit RK-3 pistol grip | Missing Cargo | Skier | 3 |
| 0.47 | medium | Magpul MOE Carbine rubber buttpad | Missing Cargo | Skier | 4 |
| 0.45 | medium | Magpul AFG tactical foregrip (Olive Drab) | Gunsmith - Part 17 | Mechanic | 3 |
| 0.45 | medium | AR-15 Hera Arms CQR pistol grip/buttstock | Gunsmith - Part 17 | Mechanic | 4 |
| 0.45 | medium | AK CNC Warrior 5.56x45 muzzle device adapter | Gunsmith - Part 17 | Mechanic | 3 |
| 0.45 | medium | AKM/AK-74 Hera Arms CQR47 pistol grip/buttstock | Gunsmith - Part 17 | Mechanic | 4 |
| 0.44 | medium | Tokarev AVT-40 7.62x54R automatic rifle | Glory to CPSU - Part 2 | Prapor | 3 |
| 0.44 | medium | Lone Star TX-15 DML 5.56x45 carbine | Gunsmith - Part 16 | Skier | 3 |
| 0.41 | medium | KAC QDSS NT4 5.56x45 sound suppressor (Black) | Cargo X - Part 2 | Peacekeeper | 3 |
| 0.41 | medium | VSS/VAL TOZ 6P29M mount | Gunsmith - Part 15 | Jaeger | 4 |
| 0.40 | medium | AR-15 Magpul PRS GEN3 stock (Black) | Gunsmith - Part 14 | Peacekeeper | 3 |
| 0.38 | medium | KAC PRS/QDC 7.62x51 sound suppressor | Gunsmith - Part 13 | Peacekeeper | 3 |
| 0.38 | medium | Zenit RK-1 tactical foregrip on B-25U mount | Beyond the Red Meat - Part 2 | Skier | 4 |
| 0.36 | medium | AK-74 Hexagon 5.45x39 sound suppressor | Humanitarian Supplies | Skier | 2 |
| 0.35 | easy | AK CSS knurled charging handle | Beyond the Red Meat - Part 1 | Skier | 3 |
| 0.34 | easy | ORSIS T-5000M 7.62x51 bolt-action sniper rifle | Lend-Lease - Part 1 | Skier | 3 |
| 0.29 | easy | TDI KRISS Vector Gen.2 9x19 submachine gun | Vitamins - Part 2 | Skier | 3 |
| 0.28 | easy | Mosin Rifle 7.62x54R ProMag Archangel OPFOR 10-round magazine | The Tarkov Shooter - Part 8 | Jaeger | 4 |
| 0.28 | easy | Mosin Rifle ProMag Archangel OPFOR PRS chassis | The Tarkov Shooter - Part 8 | Jaeger | 4 |
| 0.28 | easy | SIG Sauer ROMEO4 reflex sight | Fishing Gear | Peacekeeper | 2 |
| 0.28 | easy | B&T MP9 9x19 submachine gun | Tigr Safari | Peacekeeper | 1 |
| 0.25 | easy | TDI KRISS Vector Gen.2 .45 ACP submachine gun | Friend From the West - Part 2 | Skier | 3 |
| 0.25 | easy | Mosin Rifle Witt Machine 7.62x54R muzzle brake | The Tarkov Shooter - Part 7 | Jaeger | 3 |
| 0.24 | easy | AK GP-25 accessory kit recoil pad | Gunsmith - Part 6 | Prapor | 2 |
| 0.24 | easy | AKS-74U PBS-4 5.45x39 sound suppressor | Shaking Up the Teller | Prapor | 3 |
| 0.23 | easy | Mosin Rifle Tacfire pistol grip | The Tarkov Shooter - Part 4 | Jaeger | 2 |
| 0.23 | easy | Mosin Rifle AIM Sports Tri-Rail mount | The Tarkov Shooter - Part 4 | Jaeger | 2 |
| 0.22 | easy | Mosin Rifle ATI Monte Carlo stock | The Tarkov Shooter - Part 6 | Jaeger | 3 |
| 0.22 | easy | Benelli M3 Super 90 12ga dual-mode shotgun | The Survivalist Path - Wounded Beast | Jaeger | 2 |
| 0.20 | easy | Mosin Rifle Texas Precision Products 7.62x54R muzzle brake | The Tarkov Shooter - Part 5 | Jaeger | 3 |
| 0.17 | easy | Mosin Rifle AIM Sports Recoil Pad | The Tarkov Shooter - Part 3 | Jaeger | 2 |
| 0.17 | easy | Mosin Rifle Tacfire Tanker Style 7.62x54R muzzle brake | The Tarkov Shooter - Part 3 | Jaeger | 2 |
| 0.16 | easy | SOK-12 12ga SAI-02 10-round magazine | Chemical - Part 2 | Jaeger | 2 |
| 0.15 | easy | VOMZ Pilad P1x42 Weaver reflex sight | Golden Swag | Skier | 1 |
| 0.14 | easy | Mosin Rifle AIM Sports MNG rail mount | The Tarkov Shooter - Part 2 | Jaeger | 1 |
| 0.11 | easy | AK 100-series polymer handguard | Gunsmith - Part 2 | Prapor | 1 |
| 0.07 | easy | Saiga-9 9x19 carbine | Supplier | Skier | 1 |
| 0.06 | easy | AR-15 Windham Weaponry Rail Gas Block | Gunsmith - Part 1 | Mechanic | 1 |
| 0.00 | easy | Aklys Defense Velociraptor .300 Blackout assault rifle | Protect the Sky | Mechanic | 4 |
