# LotR2 Remake — Checklist complet engine

Sursă: decompilarea `Lords2.exe` din proiectul [open-lords2](https://github.com/brettins/open-lords2) ([kingdom.md](https://github.com/brettins/open-lords2/blob/main/docs/kingdom.md), [rules.md](https://github.com/brettins/open-lords2/blob/main/docs/rules.md), [mechanics.md](https://github.com/brettins/open-lords2/blob/main/docs/mechanics.md)). Stare la 23 sep 2026.

## Cum folosești lista

Fiecare regulă e un `[ ]` de bifat contra codului: bifat = implementat identic și acoperit de un test.

| Marcaj | Înseamnă | Cât ne bazăm |
| --- | --- | --- |
| [V] | citit din binar și confirmat de o a doua sursă (string din joc, manual, save real) | implementăm exact |
| [D] | citit doar din codul decompilat | implementăm, cu test marcat "de reconfirmat" |
| [I] | dedus, nedovedit | parametru configurabil, nu hardcodat |

La verificare, fiecare item primește una din trei stări: **OK** (identic, bifat `[x]`), **DIFERIT** (există dar altă formulă — notăm diferența și fișierul) sau **LIPSĂ**. Unde originalul are un bug, lista spune explicit dacă îl copiem sau nu.

## Arhitectură și determinism

Simularea trebuie să fie pură, pe întregi și deterministă, separată complet de scenele Godot.

- [ ] Simularea stă într-un assembly C# fără referințe la `Godot.*` (doar POCO/records), testabil cu xUnit fără editor
- [ ] Zero `float`/`double` în reguli; doar `int` cu helper-ele originalului: `Pct(x, p) = x * p / 100` și `DivCeil(a, b)`
- [ ] Un PRNG propriu, seeded, salvat în save — nu `GD.Randf()` / `System.Random` partajat. Originalul pășește două LFSR de 31 biți o dată pe sezon și publică valori mascate `& 0x7FFF`, `& 0x7F`, `& 7` [V]
- [ ] Nicio iterație pe `Dictionary`/`HashSet` în reguli — comitatele se procesează în ordinea indexului (1…N)
- [ ] Toate constantele și tabelele într-un `EconomyRules` data-driven (JSON/`Resource`), nu magic numbers
- [ ] Fiecare pas din pipeline = funcție `(WorldState, Rules, Rng) -> WorldState`, cu test unitar propriu
- [ ] Starea UI ("preview"-urile din panouri) calculată separat de starea de simulare (ex. `taxShown` vs `taxCollected`)
- [ ] Save/load = serializarea completă a `WorldState` + seed-ul RNG; test: 20 de sezoane, save la 10, reload, sezonul 20 iese identic
- [ ] Trei opțiuni de joc ca flag-uri care schimbă reguli: Advanced Farming, Armies Eat (foraging), Exploration (fog of war) [V]

## Turn machine și ordinea pipeline-ului

Un turn = un sezon, cu 7 faze; sfârșitul de sezon rulează ~30 de pași într-o ordine fixă — ordinea e regula.

**Cele 7 faze ale unui turn** [V]

- [ ] 1 Comitate neutre: AI setează taxe și câmpuri pentru comitatele fără stăpân
- [ ] 2 Asedii (nu mișcarea armatelor!) — validare, construcție mașini, asalt
- [ ] 3 Transporturile de provizii merg spre destinație
- [ ] 4 Turul jucătorilor: omul dă ordine prin UI, AI-urile rulează programul de 14 pași
- [ ] 5 Gloatele revoltate se mișcă
- [ ] 6 Negustorii fac următorul segment de rută
- [ ] 7 Sfârșit de sezon (pipeline-ul de mai jos), apoi înapoi la 1
- [ ] Unitățile se mișcă pe tot parcursul turnului, un tile pe tick, indiferent de fază [V]

**Calendarul** [V]

- [ ] Jocul nou începe iarna 1268; sezoanele 1–4 = primăvară, vară, toamnă, iarnă; anul se schimbă când începe iarna
- [ ] Sezonul se setează la cel **care începe** înainte de a rula economia — o regulă pe "primăvară" se execută la finalul iernii

**Pipeline-ul de sfârșit de sezon, în ordinea exactă** [V]

| # | Pas | Ce face |
| --- | --- | --- |
| 0 | AI fields | AI își aranjează câmpurile și munca |
| 1 | Clock | avansează sezon/an/contor turn |
| 1a | Ledger | snapshot aur/resurse pentru afișarea diferențelor |
| 2 | Events | un eveniment random per comitat eligibil |
| 3 | Weather | vremea pe comitat |
| 4 | **Tax** | colectare + termenul de fericire din taxe |
| 5 | Wages | solda armatei, faliment |
| 6 | **Rations** | aici se consumă hrana |
| 7 | Health | contorul de sănătate |
| 8 | **Happiness** | suma termenilor |
| 9 | Unrest | contorul de revoltă |
| 9a | Secession | comitatele tăiate de bloc devin independente |
| 9b | Field census | recalculează câmpurile din tile-urile hărții |
| 10 | Fertility | |
| 11 | Reclamation | recuperare câmpuri |
| 12 | **Grain** | semănat / crescut / recoltat după sezon |
| 13 | **Herd** | nașteri/decese vite |
| 14–17 | Industry | arme ÎNTÂI, apoi fier, piatră, lemn |
| 18 | Castle | construcție castele |
| 19 | **Labour** | realocă toată lumea de la zero |
| 20 | Migration | |
| 21 | **Population** | nașteri și decese |
| 22 | Merchants | cine stă unde |
| 23 | Muster | recalculează oamenii sub arme |
| 24 | Events expire | modificatorii evenimentelor țin exact un sezon |
| 25 | **Labour din nou** | cu nou-născuții și recruții |
| 26 | History | inel de 400 sezoane × 16 comitate {pop, fericire} |
| 27 | Panels refresh | estimări, preview rații și taxe |

- [ ] Ordinea de mai sus e implementată ca listă explicită de pași, nu apeluri împrăștiate
- [ ] Fierarul consumă fierul din sezonul **trecut** (armele se fac înainte de minerit) [V]
- [ ] Scorul NU e în pipeline — se recalculează la schimbarea de fază și la recount-ul de putere [V]

## Taxe și tezaur

Venitul din taxe e `Pct(Pct(pop, base), rată)` pe sezon, unde `base` depinde doar de castel.

```
take = floor( floor(pop * base / 100) * rate / 100 )
```

| Castel | base | Echivalent |
| --- | --- | --- |
| niciunul | 320 | 3,2 coroane/om la 100% |
| Wooden Palisade | 480 | ×1,50 |
| Motte & Bailey | 560 | ×1,75 |
| Norman Keep | 640 | ×2,00 |
| Stone Castle | 720 | ×2,25 |
| Royal Castle | 800 | ×2,50 |

- [ ] Formula de mai sus, cu rotunjirea `Pct` în doi pași (nu `pop * 3.2 * rate`) [V]
- [ ] Rata maximă 50, minimă 0; la fericire 0 rata nu mai poate crește [V]
- [ ] Fericire locală: `dHapTax = (5 − rate) + empireTerm` [V]
- [ ] `empireTerm` = suma, peste toate comitatele regatului, a unui **tabel** pe rată — 0 până la rata 19, apoi −1 (20–23), −2 (24–27), −3 (28–31), −4 (32–34), −5 (35–37), −6 (38–39), −7 (40–41), −8 (42–43), apoi −1 pe punct până la −15 la rata 50 [V]
- [ ] Originalul adună `empireTerm` într-un `sbyte` fără clamp (overflow) — nu copiem: `int` + clamp
- [ ] Castelul **în construcție**: se folosește minimul dintre castelul existent și cel în lucru când flag-ul de construcție e activ [D]
- [ ] Comitatele neutre își țin taxa într-o pungă proprie (`neutralPurse`), cu care cumpără mâncare de la negustori [V]
- [ ] Evenimentul „Highwaymen” anulează taxa sezonului doar dacă taxa afișată ≥ 10 [V]
- [ ] Panoul „People pay” e un preview (populația de acum); colectarea folosește populația de la momentul colectării [V]
- [ ] Tezaurul e global per regat: aur, fier, piatră, lemn, 6 tipuri de arme; hrana e locală per comitat [V]

## Hrană și rații

Lactatele primele și gratis (5 oameni/vacă); restul se împarte după un singur procent între vită tăiată (10 oameni/cap) și grâne (6 oameni/sac); dacă nu ajunge, rația coboară o treaptă.

| Treaptă | Necesar | Fericire (`3L − 8`) |
| --- | --- | --- |
| 0 None | 0 | −8 |
| 1 Quarter | `DivCeil(pop, 4)` | −5 |
| 2 Half | `DivCeil(pop, 2)` | −2 |
| 3 Normal | pop | +1 |
| 4 Double | pop × 2 | +4 |
| 5 Triple | pop × 3 | +7 |

- [ ] Necesarul = `DivCeil(pop, divisor) × multiplier` din tabelul de mai sus [V]
- [ ] Pasul 1, lactate: `fedByDairy = herd × 5`, scăzut întâi, fără să omoare vite [V]
- [ ] Pasul 2, restul după `rationSplit` (0–100% din vită): vite tăiate = `DivCeil(oameni, 10)`, saci = `DivCeil(oameni, 6)` [V]
- [ ] Grânele și vita sunt **simultane și ponderate**, nu o coadă de priorități [V]
- [ ] Fiecare parte plafonată la stoc; dacă nu încape, bucla coboară treapta de la `wanted` în jos până încape [V]
- [ ] Salvăm separat `rationWanted` și `rationAchieved` [V]
- [ ] Cu Armies Eat: soldații din comitat (prieteni + dușmani) se adaugă la necesar, la rația comitatului; garnizoanele din castel NU mănâncă din stocuri [V]
- [ ] Slider-ul de split sare peste valorile care nu schimbă nimic (UI) [V]
- [ ] Grânele și vitele se transportă între comitate cu căruțe (câteva sezoane, pot fi distruse); lactatele nu [V]
- [ ] NU copiem exploit-ul „rații duble fără mâncare” — test: 0 vite, split 0%, rație Double, stoc insuficient → rația trebuie să coboare

> Conflict: `rules.md` zice la un moment dat „un sac la zece oameni”, dar tabelul decompilat și save-ul reprodus dau 6 oameni/sac. Mergem pe 6.

## Sănătate

Contor 0–100 mișcat de un tabel `[rație][bandă]`, apoi tăiat în 5 benzi.

| Rație \ bandă | Diseased | Sick | Average | Good | Perfect |
| --- | --- | --- | --- | --- | --- |
| None | −8 | −10 | −13 | −16 | −20 |
| Quarter | −4 | −6 | −9 | −12 | −15 |
| Half | −2 | −4 | −6 | −8 | −12 |
| Normal | +8 | +4 | +2 | +1 | −1 |
| Double | +12 | +8 | +4 | +2 | 0 |
| Triple | +20 | +12 | +6 | +3 | +1 |

- [ ] Tabelul de mai sus, indexat cu rația **obținută** și banda curentă [V]
- [ ] Clamp 0–100, apoi benzi: ≤10 Diseased, ≤35 Sick, ≤65 Average, ≤90 Good, ≤100 Perfect [V]
- [ ] Fericire din bandă: −10, −5, 0, +1, +2 [V]
- [ ] Perfect scade sub orice rație mai mică decât Double (trebuie să iasă din test) [V]
- [ ] Ciuma lovește sănătatea și populația — vezi Evenimente

## Fericire

Total cumulat: fiecare sezon adună `dHapTax + dHapHealth + dHapRation`, clamp 0–100; berea și recrutarea se aplică imediat.

**Pasul de sezon** [V]

- [ ] `happinessLast = happiness; happiness += dHapTax + dHapHealth + dHapRation`
- [ ] Comitat cu 0 populație deținut de AI → fericirea setată la 50
- [ ] Comitat neutru sub 75 → +5 („From events”)
- [ ] Clamp 0–100; se ține și suma cumulată + media pe tot jocul
- [ ] Termenii „army” și „ale” din panou se resetează la 0 în fiecare sezon (afișaj)
- [ ] Test de echilibru: Perfect + Normal se menține la taxă 8; Good + Normal la taxă 7

**Berea** [V]

- [ ] +1 fericire pentru fiecare 10% din populație cheltuit în coroane (1 butoi = 1 coroană), maximum +5
- [ ] Plafonul se aplică pe `5 − aleGiven`, fericirea se aplică imediat, clamp la 100
- [ ] `aleGiven` se resetează în fiecare sezon (corecția din mechanics.md + Readme oficial), nu pe tot jocul

**Recrutarea** [V]

- [ ] Cost în fericire pe **procentul** recrutat: 5%→2, 10%→5, 20%→10, 25%→19, 33%→37, 50%→90, 60%+→101
- [ ] Dacă fericirea e mai mică decât costul, ajunge la 0
- [ ] Bug original: sub 50 de oameni indexul iese din tabel → recrutare gratuită; nu copiem, clamp la indexul maxim
- [ ] La fericire 0 nu se mai poate recruta (manual)

## Populație și migrație

**Nașteri și decese** [V]

```
base   = BirthLadder(pop)                      // %
factor = hap<26 ? 25 : hap<51 ? 50 : hap<76 ? 75 : hap<100 ? 100 : 120
rate   = Pct(base, factor)
births = Pct(pop, rate);   if births==0 && rate!=0  births = 1
deathR = DeathByHealth[band] + DeathBySeason[season]
deaths = Pct(pop, deathR); if deaths==0 && deathR!=0 deaths = 1
if band == Diseased        deaths += 2
if rate < deathR deaths += 1 else births += 1
// swing eveniment (ciumă / wedding fever) — vezi Evenimente
pop += births - deaths; if pop < 1 → pop = 0
pop -= emigrants; pop += immigrants
```

| pop ≤ | 40 | 80 | 100 | 250 | 500 | 700 | 800 | 900 | 1000 | 1100 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| natalitate % | 100 | 70 | 50 | 30 | 20 | 15 | 14 | 13 | 12 | 11 |

| pop ≤ | 1200 | 1300 | 1400 | 1500 | 1600 | 1700 | 1800 | 1900 | 2000 | 3000 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| natalitate % | 10 | 9 | 8 | 7 | 6 | 5 | 4 | 3 | 2 | 1 |

- [ ] Pseudocodul de mai sus, în ordinea exactă
- [ ] Scara de natalitate (cele două tabele) [V]
- [ ] Mortalitate pe bandă: Diseased 35, Sick 20, Average 8, Good 3, Perfect 0 (%) [V]
- [ ] Mortalitate pe sezon: primăvară 4, vară 0, toamnă 2, iarnă 8 (%) — se adună [V]

**Migrația** [V]

- [ ] Vecinul cel mai fericit din lista de adiacență; dacă e mai fericit: `pct = Pct(best − hap, (100 − hap) / 3)`, `movers = min(Pct(pop, pct), 100)`
- [ ] Comitat neutru: `movers /= 2`
- [ ] La fericire 100 nu emigrează nimeni; diferențe mici (72 vs 77) nu mută pe nimeni
- [ ] Adiacența = listă de vecini definită per hartă (nu din tile-uri), simetrică — test pe harta noastră
- [ ] Bug original în lista de surse de imigranți (doar text panou) — nu copiem

## Muncă: 9 joburi și alocatorul

Populația se realocă de la zero de două ori pe sezon, după procente salvate, plafonate de un „useful ceiling” per job; restul devine Idle.

Joburi [V]: 0 grâne, 1 vite, 2 recuperare câmpuri, 3 construcție castel, 4 fier, 5 piatră, 6 lemn, 7 fierar, 8 Idle townsfolk.

- [ ] Fiecare job are 3 întregi: `workers`, `wanted` (prag minim, doar afișaj), `usefulCeiling` [V]
- [ ] Ceiling: grâne și vite prin **căutare** pe `workers = 0…pop`; fier/piatră/lemn = 100.000; resursă absentă sau industrie oprită = 0 [V]
- [ ] Split: `industryShare` (start 25%) + 8 procente: 3 pe fermă (0–2) = 100 și 5 pe industrie (3–7) = 100 [V]
- [ ] Alocare: fiecare job primește `Pct(jumătatea lui, share)` sau cât permite ceiling-ul; resturile se plimbă pe joburile cu loc (grâne și vite 5 ture pentru una a recuperării); restul → Idle [V]
- [ ] Invariant testat: suma celor 9 joburi == populația, în fiecare comitat, în fiecare sezon [V]
- [ ] Alocatorul nu citește deloc `wanted` [V]
- [ ] Rulează de 2 ori în pipeline + imediat după vopsirea unui câmp și după pornirea/oprirea unei industrii [V]
- [ ] Drag de oameni rescrie procentele; drag pe o industrie oprită o **pornește** (dacă există resursa) [V]
- [ ] Castelul are ceiling 0 până când toate materialele sunt livrate (Readme oficial) [I]
- [ ] Afișaj: sub prag = număr roșu + figuri fantomă; peste ceiling = surplus desenat ca Idle; o iconiță = `ceil(pop/25)` oameni

## Câmpuri, grâne, fertilitate, vreme

**Câmpuri** [V]

- [ ] Max 20 câmpuri/comitat (tipic 8–16); contoarele fallow/grâne/pășune/deșert/în-recuperare sunt un **cache** recalculat din tile-uri
- [ ] Vopsire prin click pe hartă: fallow / grâne / pășune; pe deșert: începe recuperarea / abandonează; câmp lovit de vreme = fără meniu
- [ ] Toate comitatele încep cu 0 câmpuri de grâne
- [ ] Recuperare: progres 0–800, max 200/câmp/sezon, 1 punct = 1 muncitor-sezon; întâi câmpul cel mai avansat; surplusul trece mai departe
- [ ] Seceta/inundația transformă un câmp în **deșert** (nu fallow)

**Ciclul grânelor** [V]

- [ ] Final de iarnă: semănat. De la 10 saci/câmp la 1, primul care satisface `stoc ≥ câmpuri × saci` și `muncă ≥ 12 × câmpuri × saci / div` (div = 5 cu AF, 2 fără); se scad sacii; `crop = saci × 12`
- [ ] Fallback: dacă nici 1 sac/câmp nu merge, seamănă cel mult 10 saci pe tot comitatul
- [ ] Final de primăvară și vară: `crop = min(crop, muncă × 10)` (AF) sau `× 2` (fără), apoi `crop += Pct(crop, fertilitate / 2)`
- [ ] Final de toamnă: recoltă `min(crop, (muncă/2) × 3)` cu AF, sau `muncă × 2` fără; intră în stoc la începutul iernii
- [ ] Câmp de grâne pierdut în an: recolta scalată cu `câmpuriAcum / câmpuriSemănate`; câmpuri noi nu produc până la următoarea semănare
- [ ] Bug original: la recoltă, pe 4 din 6 vremuri, se folosește cultura în picioare (Sunny = 3/2 din tot, oricâți secerători) — nu copiem

**Fertilitate** (doar cu Advanced Farming) [V]

- [ ] `fertility += 6 × fallow − 3 × grâne`, clamp −100…+100; fără AF = 0
- [ ] Un fallow la 2 câmpuri de grâne = break-even; pășunile nu contează
- [ ] Maparea −100…+100 pe cele 7 etichete nu e cunoscută — o definim noi [I]

**Vremea** [V]

- [ ] Acumulator de uscăciune per comitat: +8 primăvara, +24 vara, +12 toamna, −12 iarna, minus jitter `(rnd & 0x7F) / 8` (0–15)
- [ ] Un comitat random + vecinii primesc swing extra (vecinii jumătate) + modificator de climă pe bandă (0–4, după indexul comitatului)
- [ ] Benzi: <5 Flooding (reset 30), <20 Storms, <70 Cloudy, <95 Sunny, altfel Drought (reset 70); iarna/primăvara Drought → Frost, Sunny peste 74 → Frost
- [ ] Fără Advanced Farming: vremea e mereu Cloudy
- [ ] Seceta/inundația strică un câmp (devine deșert)
- [ ] Efect pe grâne: semănat — Flooding ¼, Frost/Storms ½; creștere — Sunny ×3/2, Drought/Flooding ½; recoltă — Sunny ×3/2, Flooding ¼, Frost/Storms ½ [D]

## Vite

| Vite / câmp pășune | Eticheta din joc | Nașteri | Decese |
| --- | --- | --- | --- |
| 1–10 | Low herd crowding | 14% | 1% |
| 11–20 | Average herd crowding | 9% | 3% |
| 21–30 | Herd overcrowded | 5% | 5% |
| 31+ sau fără pășune | Massive overcrowding!! | 2% | 7% |

- [ ] Tabelul de aglomerare; ratele sunt la 10.000 aplicate pe `herd × 100` [V]
- [ ] Personal: `labour / (herd × 3)`; sub 100% deficitul se adaugă la mortalitate (la 0 îngrijitori ~34%); beneficiul plafonat la 200% [V]
- [ ] Fără nicio pășune: se pierde jumătate din cireadă, sau toată sub 6 capete [V]
- [ ] Bonus de natalitate pentru cirezi mici, trepte la 5, 10, 25 capete (bug B98: 4 vite pot face mai mulți viței decât 5 — de decis) [V]
- [ ] Sezonier: primăvara ×1,5 nașteri, iarna ×1,5 decese [V]
- [ ] Vreme: Frost −2%, Drought −10%, Sunny +5%, Cloudy 0, Storms −5%, Flooding −10% [V]
- [ ] Nașteri = `herd × rate / 10000` cu împărțire întreagă [V]
- [ ] Ceiling îngrijitori = căutarea care maximizează `births − deaths`; plafon absolut 6/cap [V]
- [ ] Forecast-ul din sidebar = nașteri − decese − consum, fără vreme și evenimente [V]
- [ ] Cumpărarea de vite într-un comitat fără pășune transformă automat un fallow (sau câmp de grâne) în pășune [D]

## Industrie și castele

Formulele exacte de producție și costurile de castel sunt în `kingdom.md` §7.4–7.5 — de citit înainte de a bifa.

**Industrie**

- [ ] 4 situri per comitat: lemn, fier, piatră, fierar; fiecare cu switch on/off prin click pe clădire [V]
- [ ] Resursele unui comitat vin de pe hartă (plasarea siturilor la încărcare) [V]
- [ ] O armată inamică peste un sit îl dezactivează 3 sezoane [V]
- [ ] Eficiență per sit: ~15% la start, crește compus pe sezon până la 100% cât există măcar un muncitor; la 0 revine la 15%; tabel separat fără AF [D] — formula exactă: §7.4
- [ ] Producție = muncitori × rată × eficiență — rata per industrie: §7.4 [I]
- [ ] Fierarul face un singur tip de armă per comitat; toți fierarii împart pool-ul global de lemn/fier, împărțit la câți sunt porniți (Readme) [V]
- [ ] Costuri arme (fier + lemn), din ghiduri, de confirmat în `g_weaponCost`: buzdugan 4+4, suliță 3+6, sabie 10+3, arc 0+13, arbaletă 10+6, armură cavaler 18+4 [I]
- [ ] Ordinea: arme, apoi fier, piatră, lemn [V]

**Castele**

- [ ] 5 tipuri + „niciunul” (0–5), cu multiplicatorul de taxă [V]
- [ ] Costuri (piatră/lemn), din wiki, de confirmat: Palisade 40/400, Motte & Bailey 80/800, Norman Keep 1000/200, Stone 2000/400, Royal 3000/800 [I]
- [ ] Construcția nu primește muncitori până când **toate** materialele sunt livrate; oprirea construcției alege ce castel primește materialele întâi (Readme) [V]
- [ ] Upgrade/downgrade; downgrade-ul poate elibera sau cere materiale (manual)
- [ ] Castel nou = garnizoană gratuită de arcași, scăzută din populație; capacitate per tip [V]
- [ ] Reparații după asediu: efect doar la 100% (manual)
- [ ] Un castel în construcție nu contează la scor [V]
- [ ] Designer-ul de castel al originalului — nedocumentat; îl facem al nostru sau îl omitem în v1

## Evenimente random

- [ ] Pachet de 256 sloturi, 24 evenimente distincte, tras la pasul Events [V] — lista și ponderile: `kingdom.md` §8.1
- [ ] Bug original: comitatele cu index par sunt exceptate — recomand să nu copiem [V]
- [ ] Modificatori procentuali per comitat pentru populație, grâne, cireadă („eaten by rats”, „taken by wolves”, „found as surplus”…), șterși la „Events expire” [V]
- [ ] Ciuma: +20% (vară), 30% (primăvară, toamnă), 40% (iarnă) din decesele sezonului + 10, plafon 20% din comitat [V]
- [ ] Wedding fever: la fel pe nașteri, 60/50/40/30% primăvară→iarnă [V]
- [ ] Doar ciuma și wedding fever mișcă oameni [V]
- [ ] Highwaymen: fără taxe în sezon, doar dacă taxa afișată ≥ 10 [V]
- [ ] Flag „scrisoare necitită” care așteaptă până e afișată [V]
- [ ] Textele le scriem noi (nu copiem textele originale)

## Revoltă, secesiune, faliment

**Revolta** [V]

- [ ] Contor `unrest` 0–4 per comitat
- [ ] Om: primul sezon sub 30 → doar avertisment; de la al doilea, sub 25 contorul urcă cu 1 + mesaje 1–4; la fericire ≥ 25 reset la 0
- [ ] AI: ≥ 41 reset, 11–40 coboară, sub 1 urcă; fără mesaje
- [ ] Revolta doar în sezonul în care contorul a urcat la 4
- [ ] 30% din populație devine gloată neînarmată (moral 50) pe un tile de drum liber (sau teren deschis) în raza de 3; comitatul devine neutru
- [ ] Fără tile liber: contorul rămâne la 4, fără revoltă
- [ ] Gloata scade fericirea comitatelor prin care trece (manual); recucerire prin capturarea orașului

**Secesiunea** [V]

- [ ] Fiecare regat păstrează doar blocul contiguu cu cea mai mare populație; restul devin neutre în același sezon
- [ ] „Contiguu” = lista de vecini, nu tile-urile
- [ ] La egalitate câștigă blocul găsit ultimul
- [ ] Doar omul primește mesajul („Your lands divide”)
- [ ] Devenire neutru: owner 0, industrii oprite, apoi realocare muncă + rații + preview taxe

**Falimentul** [V]

- [ ] Scară de 6 sezoane: mercenarii pleacă imediat, apoi dezertări, la final toate armatele se desființează
- [ ] O plată completă resetează scara; după dezertarea totală contorul revine la 0
- [ ] Detaliile fiecărei trepte: `kingdom.md` §7.7

## Comerț: negustori

- [ ] 6 negustori, rute fixe per hartă, un segment pe turn (faza 6) [V]
- [ ] 14 bunuri; oaia și lâna au preț 0 și nu se pot tranzacționa (subsistem tăiat) — le putem omite [V]
- [ ] Preț cumpărare = bază + moralul negustorului ca procent (mereu 100 → exact dublu), minim 1; berea e excepție [V]
- [ ] Prețuri vânzare/cumpărare (ghiduri, de confirmat): vacă 12/24, sac 2/4, piatră 2/4, fier 1/2, lemn 1/2, buzdugan 10/20, suliță 13/26, arc 16/32, sabie 23/46, arbaletă 24/48, armură cavaler 44/88, bere 1 [I]
- [ ] Fără umpleri parțiale [V]
- [ ] Comitatele neutre cumpără mâncare din punga lor de taxe [V]
- [ ] Grânele și vitele cumpărate intră în comitatul negustorului; resursele și armele în tezaurul global [V]

## Armate pe hartă

**Recrutare și echipare**

- [ ] 7 tipuri: țărani, buzdugănari, sulițași, spadasini, arcași, arbaletrieri, cavaleri; recrutare → armurărie → creare armată [V]
- [ ] Recruții se scad din populație; cost în fericire din tabelul de recrutare [V]
- [ ] Desființare: oamenii intră în comitatul unde se desființează armata, armele în tezaur (manual)
- [ ] Split: minim 50 de oameni, excepție split într-un castel (Readme); 3 moduri [V]
- [ ] Merge până la limită (1500 după comunitate) [I]
- [ ] Mercenarii nu se amestecă cu trupe proprii (comunitate) [I]

**Mișcare**

- [ ] Hartă de cost + flood fill + extractor de drum, comune cu negustorii și transporturile [V] — `armies.md` §2
- [ ] Buget de mișcare per turn (15 după comunitate: drum 1, iarbă 2; pădure/apă/munte impracticabil) [I]
- [ ] Călcarea câmpurilor inamice distruge cultura/vitele; armată inamică pe sit industrial îl oprește 3 sezoane [V]
- [ ] Orice ordin de mișcare reușit ridică asediul [V]
- [ ] Exploration: armata dezvăluie 13×13 tile-uri unde se oprește; ce e văzut rămâne văzut [V]

**Solde și foraging**

- [ ] Solda la pasul Wages; neplata → faliment [V] — formula: `armies.md` §6.4 (comunitate: ~1/3 coroană/soldat/an) [I]
- [ ] Armies Eat: armata mănâncă din comitatul în care stă, înaintea populației; trupele ostile se servesc singure [V]
- [ ] Armată mai mare decât toată cămara comitatului: avertisment, apoi 10% dezertare/sezon, apoi dizolvare [V]

**Cucerire**

- [ ] Pas pe tile-ul de castel = atac [V]
- [ ] Castel **și** garnizoană străină = doar prin asediu [V]
- [ ] Comitat neutru sub fericire 11 se predă fără luptă [V]
- [ ] Cucerirea scade puternic fericirea (manual)
- [ ] Prizonieri/răscumpărare — necercetat; omitem în v1

**Mercenari** [V]

- [ ] 12 bande fixe (una per naționalitate) care se plimbă pe hartă; o angajezi pe cea din comitatul tău
- [ ] Mărimi, prețuri, tipuri din 6 tabele statice — `armies.md` §5

## Asedii și bătălii

**Autocalc (v1)** [V]

- [ ] Raport de putere → scară de 10 trepte → pierderi; la luptă egală câștigătorul rămâne cu ~o zecime — tabel: `armies.md` §7.2
- [ ] Toate bătăliile AI vs AI trec prin autocalc
- [ ] „Will you take the field?”: turnul se suspendă până răspunde omul; fără timeout în single-player
- [ ] Rezultatul trece înapoi în campanie (atenție: variabila „loser” din original ține de fapt câștigătorul)

**Asediu pe campanie** [V]

| Mașină | Muncă (om-sezon) | Maxim comandat |
| --- | --- | --- |
| Catapultă | 200 | 4 |
| Turn de asediu | 200 | 4 |
| Berbece | 400 | 2 |

- [ ] Mașinile se construiesc pe loc; timp = `ceil(muncă / oameni)` sezoane
- [ ] Sub Norman Keep se asaltă fără mașini; peste, fără mașini asediul e ridicat automat
- [ ] În autocalc castelul valorează 160%, 200%, 250%, 320%, 400% din garnizoană, după tip
- [ ] Ulei fierbinte: 1, 2, 3, 4, 6 oale după tip
- [ ] Pierderea asaltului nu distruge armata, doar asediul

**Bătălia live (după v1)** [V]

- [ ] Figuri, unități, formații, max 80 figuri
- [ ] Melee: apărarea = timpul de recuperare; lovitura grea o dată per figură (buzdugan 300, cavaler 200, sabie 100); 100 hp/victimă, mașini 160
- [ ] Armura doar contra proiectilelor; proiectilele zboară efectiv (Bresenham) și lovesc ce e în cale
- [ ] AI: puterea sa ca % din a ta minus 100, recalculat la ~101 cadre; atacă peste 5, garnizoana iese doar peste 260
- [ ] Sfârșit: o tabără la 0 oameni sau retragere — fără moral, fără limită de timp
- [ ] Asediu live: poarta 20.000 hp, zidul 5.000 hp/bucată, un om la ușa donjonului câștigă
- [ ] Detalii: `battle.md`, `battle-ai.md`

## AI și diplomație

**Programul AI, per turn** [V]

- [ ] 0 recount · 1 inbox · 2 diplomație · 3 taxe · 4 vânzări/cumpărări · 5 câmpuri după stilul lordului · 6 castele · 7 garnizoane/comitat de adunare · 8 gol · 9 armata principală · 10 raid de ~50 țărani · 11 avansează armatele · 12 arme + industrii + muncă · 13 tachinări · 14 recount totaluri

**Personalități** [V]

| Lord | Cel mai bun castel (prag aur) | Simultan | Recrutează | Ofertă alianță |
| --- | --- | --- | --- | --- |
| Knight | Royal (10.000) | 4 | 30% | la 12 turi |
| Baron | Stone (4.000), niciodată Royal | 3 | 30% | la 10 turi |
| Countess | Stone (2.000), niciodată Royal | 2 | 40% | la 8 turi |
| Bishop | Royal (2.000) | 1 | 50% | la 4 turi |

- [ ] Tabel + scara de castele per lord (Knight: palisade 200, keep 1000, royal 10.000; Bishop: keep 100, royal 2000)
- [ ] Aur gratuit/turn pe dificultăți 0–3: Knight 0/400/700/1200, Baron 100/500/800/1400, Countess 0/400/700/1200, Bishop 250/600/1100/1800, omul 0
- [ ] Sub 3 comitate tabel mai mic (Bishop 1800 → 600); oameni/vite/grâne gratuite doar la 1–4 comitate
- [ ] Asediu per lord: Knight 4 turnuri; Baron 1 berbece + 2 turnuri; Countess/Bishop 3 catapulte + 2 turnuri (+ berbece contra Stone/Royal)
- [ ] Rotă de arme pe 10 pași per lord (Baron fără arbalete)

**Diplomație** [V]

- [ ] Standing −30…+30 per pereche; +1/turn spre alte AI-uri, niciodată spre om
- [ ] 7 mesaje: cadou, compliment, insultă, oferă alianță, rupe alianța, cere ajutor, cere atac; 5 sloturi inbox; răspuns în turul următor
- [ ] Cadou comparat cu cel mai mare trimis vreodată; prea mic = −8
- [ ] Complimente: +15, +8, apoi −4 pentru fiecare după al treilea
- [ ] Alianțe exclusive; „grudge” crește per turn și rupe alianța la prag per lord
- [ ] Standing la minim → 2 avertismente, apoi război permanent
- [ ] Culori: fiecare AI ia cel mai mic scut liber, apoi lordul se alege din scut

## Scor, victorie, campanie, opțiuni

- [ ] Eliminare când `putere = 3 × comitate + armate` = 0, recalculată la începutul turnului fiecărui regat [V]
- [ ] Victoria vine când ultimul adversar e eliminat [V]

| Termen scor | Pondere |
| --- | --- |
| Castele terminate | ×50 |
| % din hartă | ×10 |
| Fericire medie | ×2 |
| Sănătate medie | ×2 |
| Oameni sub arme | ÷5 |
| Populație totală | ÷10 |
| Aur peste 2.000 | +50 fix |

- [ ] Scor conform tabelului (treptele de 5.000/10.000 sunt cod mort în original — de decis) [V]
- [ ] „Greatest noble” nedecis până în 1270 [V]
- [ ] Campanie: nu se păstrează nimic între hărți; înfrângerea repetă harta; dificultate 0,0,1,1,2,2,2,2; aur de start 5.000/2.500/1.000 [V]
- [ ] Opțiuni joc nou: aur, castel, armurărie, garnizoană, stocuri, număr de lorzi, dificultate, starea comitatului de start [V]

## Goluri și decizii

**Necunoscute chiar și în open-lords2**

- [ ] Maparea fertilitate → 7 etichete
- [ ] Livrarea materialelor la castel
- [ ] Designer-ul de castel și layout-ul pe câmpul de luptă
- [ ] Prizonieri și răscumpărare
- [ ] Umplerea șanțului în asediu
- [ ] Validare pe save-uri târzii (turn 40+, regate cu multe comitate)

**De citit încă din open-lords2**

- [ ] `kingdom.md` §7.4, §7.5, §7.6, §7.7, §8.1, §8.2, §14
- [ ] `armies.md` (solde, mișcare, autocalc, mercenari)
- [ ] `bugs.md` (catalogul bug-urilor originalului)

**Decizii de luat de noi** (bug-uri ale originalului)

- [ ] Exceptarea comitatelor pare de la evenimente
- [ ] Recolta Sunny care ignoră munca
- [ ] Recrutarea gratuită sub 50 de oameni
- [ ] Overflow-ul `sbyte` la taxa imperiului
- [ ] Treptele moarte de aur din scor
- [ ] Bonusul ciudat de natalitate la cirezi mici (B98)
