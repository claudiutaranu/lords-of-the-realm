# Audit LotR2 — codul nostru contra `lotr2-engine-checklist.md`

Stare la 23 sep 2026: working tree peste `4d7e350`, inclusiv modificările necomise de UI de bătălie (`BattlePanel`, `ArmyPanel`, `CampaignMapPage`, `Units`…) apărute în paralel, din altă sesiune. Liniile citate sunt cele din working tree. Doar citire: niciun fișier de cod nu a fost modificat.

Marcaje: `[x] OK` = implementat identic · `[ ] DIFERIT` = există, dar altfel · `[ ] LIPSĂ` = nu există.
Itemii `[I]` din checklist sunt nesiguri: o valoare diferită e notată, dar nu e numărată ca greșeală.

## Rezumat

Numărat automat din marcajele de mai jos. „din care [I]” sunt diferențe pe itemi nesiguri: notate, dar nu
considerate greșeli.

| Secțiune | OK | DIFERIT | din care [I] | LIPSĂ |
| --- | --- | --- | --- | --- |
| Arhitectură și determinism | 1 | 7 | 0 | 1 |
| Turn machine și ordinea pipeline-ului | 0 | 8 | 0 | 5 |
| Taxe și tezaur | 1 | 8 | 0 | 1 |
| Hrană și rații | 1 | 8 | 0 | 1 |
| Sănătate | 0 | 1 | 0 | 4 |
| Fericire | 0 | 7 | 0 | 6 |
| Populație și migrație | 1 | 4 | 0 | 4 |
| Muncă: 9 joburi și alocatorul | 1 | 9 | 1 | 0 |
| Câmpuri, grâne, fertilitate, vreme | 1 | 14 | 1 | 5 |
| Vite | 1 | 6 | 0 | 3 |
| Industrie și castele | 0 | 10 | 3 | 6 |
| Evenimente random | 3 | 5 | 0 | 1 |
| Revoltă, secesiune, faliment | 0 | 3 | 0 | 12 |
| Comerț: negustori | 1 | 4 | 1 | 2 |
| Armate pe hartă | 3 | 13 | 3 | 5 |
| Asedii și bătălii | 1 | 4 | 0 | 11 |
| AI și diplomație | 0 | 3 | 0 | 10 |
| Scor, victorie, campanie, opțiuni | 0 | 2 | 0 | 4 |
| Goluri și decizii | 0 | 2 | 0 | 13 |
| **Total** | **15** | **118** | **9** | **94** |

**Concluzie.** Codul de azi e un joc „în forma” lui Lords II, nu o reconstrucție a lui. Structura seamănă
(comitate, câmpuri, rații, taxe, companii, castele, piață), dar regulile sunt inventate, pe `float`, într-o
altă ordine a pipeline-ului și cu un RNG nesemănat. Aproape nimic nu e „identic”. Cele mai multe OK-uri sunt
bug-uri ale originalului pe care nu le avem. Pentru „exact ca la Lords” pipeline-ul trebuie rescris pas cu
pas, nu corectat punctual. Ordinea e la final.

Baseline: `dotnet build` curat și cele 6 suite din `scene/checks/` trec (`all checks passed`) pe working tree-ul de mai sus.

## Harta proiectului

Un singur assembly (`Realm Prototype.csproj`). Simularea stă în `scripts/Economy/`, dar fiecare fișier
de acolo are `using Godot`: `Mathf`, `RandomNumberGenerator`, `Vector2`, `GD.Load<Json>`, iar
`GameBalance` e un `Resource`.

| Rol | Unde |
| --- | --- |
| Turn loop (toate comitatele) | `scripts/Economy/TurnManager.cs:756` `AdvanceTurn()` |
| Pipeline pe un comitat | `scripts/Economy/EconomySimulation.cs:19` `RunTurn()` |
| Starea comitatului / armatei | `ProvinceEconomy.cs`, `FieldArmy.cs` |
| Reguli și constante | `GameBalance.cs` + `data/game-balance.tres`, `data/*.json` |
| Evenimente, AI, piață, luptă | `EventEngine.cs`, `LordAI.cs`, `Market.cs`, `Battle.cs` + `TurnManager.Sieges` |
| Save | `scripts/SaveGame.cs` (JSON v4) |
| Teste | `*Check.cs` → `scene/checks/*.tscn`, Godot headless |
| UI care atinge regulile direct | `CityPage`, `LabourPage`, `FieldPanel`, `TaxPanel`, `RationPanel`, `LabourBar`, `BlacksmithPage`, `RecruitsPage`… |

---

## Arhitectură și determinism

- [ ] DIFERIT — `scripts/Economy/EconomySimulation.cs:2`, `TurnManager.cs:2`, `GameBalance.cs:8` — Simularea stă în același assembly cu UI-ul și depinde de Godot: `Mathf`, `RandomNumberGenerator`, `Vector2` (`TurnManager.cs:158`), `GD.Load<Json>` (`EventEngine.cs:428`, `Fortifications.cs:59`, `Units.cs:52`, `Mercenaries.cs:124`), iar `GameBalance`/`ProvinceDefinition` sunt `Resource`. Testele rulează doar prin editorul headless, nu cu xUnit. Lista cere un assembly POCO fără `Godot.*`.
- [ ] DIFERIT — `ProvinceEconomy.cs:19` (`float Loyalty`), `:418` (`float LabourSplit`), `:434` (`float[] Fertility`), `EconomySimulation.cs:344` (`TaxDue` = `Mathf.RoundToInt(pop * float * rate * (1f + bonus))`), `:327` (`Achieved(float multiple)`, cu epsilon `0.001f` la `:332`), `:351/:356` (termenii de fericire `float`), `:136-138` (industrie `RoundToInt` pe float), `:230-233` (vite), `FieldArmy.MarchLeft/X/Y` — Regulile sunt aproape integral pe `float`, cu `RoundToInt`/`CeilToInt`. Nu există helper-ele `Pct` și `DivCeil`. Lista cere doar `int`.
- [ ] DIFERIT — `TurnManager.cs:28` (`RandomNumberGenerator` din Godot), `:88` (`_rng.Randomize()`, deci fără seed, intenționat, vezi comentariul de la `:85-87`), `SaveGame.cs:95-104` (starea RNG nu se salvează) — Un singur RNG Godot, nesemănat, folosit de `EventEngine.cs:193/318`, `Mercenaries.cs:54/83` și `Battle.cs:179-180` (`RandfRange` pe `float`). Nu e PRNG propriu și nu ține cele două LFSR-uri de 31 de biți cu ieșirile mascate `&0x7FFF / &0x7F / &7`. Comentariul de la `:85` contrazice regula: el apără imprevizibilitatea între campanii, pe care o dă și un seed aleatoriu la joc nou, salvat.
- [ ] DIFERIT — Iterări pe `Dictionary` în cod de reguli: `TurnManager.cs:127` (`PoolPurses`), `:208` (`Merge`, `from.Men`), `:460` (`Lift`), `:512` (garnizoana care flămânzește la asediu), `:548` (`Bury`), `:589` (`DefendersOf`), `:672` (`OtherCounties`), `:693` (`RealmStore`), `:777` (reset `MarchLeft`), `EconomySimulation.cs:415` (`Thin`, dezertare), `EventEngine.cs:378`, `Market.cs:183`, `ProvinceEconomy.cs:295/304/530`, `Battle.cs:171`. **Niciuna nu dă azi rezultate dependente de ordine:** fiecare cheie e tratată independent sau se adună întregi. `PoolPurses` alege doar ce obiect `Treasury` devine punga regatului, iar valoarea iese aceeași. Pașii pe comitate merg în ordinea din `_definitions` (`TurnManager.cs:767/784`, `Resented` la `:655`), iar `Battle.Order` sortează ordinal (`Battle.cs:326`). Forma nu respectă regula, deci e o capcană pentru orice pas nou care taie cu rest.
- [ ] DIFERIT — `GameBalance.cs` + `data/game-balance.tres`, `data/*.json` — Majoritatea valorilor sunt data-driven, dar sunt încă magic numbers în cod: `LordAI.cs:26/30/31/216` (`ComfortBand`, `TaxRelief`, `TaxGreed`, `WorkingStock`), `Market.cs:106` (`0.5f`), `:188` (`0.01f`), `EconomySimulation.cs:215` (`/ 2f`), `:332` (`0.001f`), `:478` (`* 10f` în `Billeted`), `ProvinceEconomy.cs:24/36` (fericire și taxă de start), `TurnManager.cs:26`. Tabelele din checklist (taxă, rații, sănătate, natalitate…) nu există ca tabele.
- [ ] DIFERIT — `EconomySimulation.cs:35-46` — Pașii sunt `private static void` care mută direct `ProvinceEconomy`. Nu sunt funcții `(WorldState, Rules, Rng) -> WorldState` și nu au test propriu pe pas. `EconomyCheck.cs` testează prin `RunTurn`/`Demand` întregi.
- [x] OK — `EconomySimulation.cs:700` — `Preview` rulează `RunTurn` pe `province.Copy()`, deci preview-ul nu atinge starea de simulare. `TaxDue` (`:344`) pe populația de acum e ce arată panoul, iar colectarea (`:361`) rulează după `Eat`, pe populația de atunci. *Notă:* nu se păstrează nicăieri un `taxShown`, iar „Highwaymen” (secțiunea Taxe) are nevoie de el. În plus, UI-ul face și reguli, nu doar preview: `BlacksmithPage.cs:66-75` și `RecruitsPage.cs:165-167` scad fier și lemn și pornesc comenzi direct din pagină.
- [ ] DIFERIT — `SaveGame.cs:95-104` — Save-ul ține `Turn`, `Difficulty`, `Provinces` (cu armatele) și `Prices`. **Nu ține** starea RNG, `News`, `_lastSeason` (tabelul de fericire al ultimului sezon), `_unheld` și nici mercenarul prezent, dacă nu e în `ProvinceEconomy`. Singurul test e un round-trip de fișier (`FortificationCheck.cs:37-62`). Lipsește testul „20 de sezoane, save la 10, reload, sezonul 20 identic”, care oricum n-ar putea trece fără RNG salvat.
- [ ] LIPSĂ — Advanced Farming, Armies Eat și Exploration nu există ca opțiuni. Implicit: fertilitatea e mereu pornită (`ProvinceEconomy.cs:434`, `RestTheLand`), soldații mănâncă mereu din comitat (`EconomySimulation.cs:251-253`, `ProvinceEconomy.Fed` `:376`), fog of war nu există.

## Turn machine și ordinea pipeline-ului

**Cele 7 faze ale unui turn**

- [ ] LIPSĂ — Comitatele neutre nu sunt simulate deloc. E o alegere deliberată: `TurnManager.cs:36-39` și `_unheld` le țin înghețate „exactly as the campaign authored it”. Nu există AI pentru taxele sau câmpurile lor.
- [ ] DIFERIT — `TurnManager.cs:476` `Sieges()`, apelat la `:835` — Asediile rulează **după** economia tuturor comitatelor, chiar înainte de `Turn++`, nu ca faza 2 înaintea turului jucătorilor. Nu se construiesc mașini de asediu (detalii la „Asedii și bătălii”).
- [ ] LIPSĂ — Nu există transporturi de provizii.
- [ ] DIFERIT — `TurnManager.cs:767-774` `LordAI.TakeTurn` — Omul dă ordine prin UI. AI-ul rulează la începutul `AdvanceTurn` și face doar economie (taxe, rații, piață). Nu e programul de 14 pași și nu mută armate (detalii la „AI și diplomație”).
- [ ] LIPSĂ — Nu există gloate revoltate.
- [ ] LIPSĂ — Nu există negustori pe rute. `Market.cs` e o piață fixă, fără deplasare.
- [ ] DIFERIT — `TurnManager.cs:756` `AdvanceTurn()` — Sfârșitul de sezon există, dar în altă ordine (vezi tabelul de mai jos).
- [ ] DIFERIT — `TurnManager.cs:158` `March`, `scripts/UI/MarchGrid.cs` — Armatele se mișcă doar când omul dă ordin în turul lui, dintr-un buget `float` `MarchLeft` resetat la începutul `AdvanceTurn` (`:777`). Nu există tick-uri și nici mișcare în afara fazei jucătorului. Armatele AI nu se mișcă deloc.

**Calendarul**

- [ ] DIFERIT — `TurnManager.cs:49-54`, `EconomyEnums.cs` `Season` — La noi jocul începe **primăvara** 1268 (`Turn 1` → `Season.Spring`, index 0), iar anul se schimbă la începutul primăverii (`(Turn-1)/4`). Lista cere început iarna 1268, sezoane 1–4 = primăvară…iarnă și schimbarea anului la începutul iernii.
- [ ] DIFERIT — `TurnManager.cs:758` (`season = CurrentSeason`), `:862` (`Turn++` după pipeline), `EconomySimulation.cs:153` — La noi pipeline-ul rulează cu sezonul **care se termină**, iar ceasul avansează la final. De aici, semănatul („Spring”) are loc la sfârșitul primăverii, cu un sezon mai târziu decât în original (sfârșitul iernii). Recolta cade în același moment fizic (sfârșitul toamnei), dar sub eticheta „Autumn”, nu „Winter”. Între semănat și recoltă grânele cresc o singură dată (vara), nu de două ori (primăvara și vara).

**Pipeline-ul de sfârșit de sezon**

Ordinea noastră, pas cu pas, pusă lângă cea din listă:

| # listă | Pas | La noi | Poziție la noi |
| --- | --- | --- | --- |
| 0 | AI fields | `LordAI.TakeTurn` (`TurnManager.cs:771`) | 1, înainte de toate ✓ |
| 1 | Clock | `Turn++` (`TurnManager.cs:862`) | **ultimul** ✗ |
| 1a | Ledger | `TurnSummary` `…Before` (`EconomySimulation.cs:21-32`) | 3a ✓ |
| 2 | Events | `EventEngine.AfterTurn` (`TurnManager.cs:803`) | **după toată economia** ✗ |
| 3 | Weather | — | LIPSĂ |
| 4 | Tax | `CollectTaxes` (`EconomySimulation.cs:41`) | **după** rații ✗ |
| 5 | Wages | `PayTheGarrison` (`:42`) | după taxe ✓, dar după rații ✗ |
| 6 | Rations | `Eat` (`:40`) | **înainte** de taxe ✗ |
| 7 | Health | — | LIPSĂ |
| 8 | Happiness | `SettleLoyalty` (`:45`) + `Resented` aplicat separat (`TurnManager.cs:795-796`) | după industrie ✗; termenul de imperiu vine după fericire, nu în taxă ✗ |
| 9 | Unrest | — | LIPSĂ |
| 9a | Secession | — | LIPSĂ |
| 9b | Field census | — (câmpurile sunt `FieldUse[]` direct, nu cache din tile-uri) | LIPSĂ |
| 10 | Fertility | `RestTheLand`, doar toamna (`:171`) | în interiorul lui Grain ✗ |
| 11 | Reclamation | `MendTheGround` (`:37`) | **după** Grain ✗ |
| 12 | Grain | `WorkTheFields` (`:36`) | **înainte** de rații și taxe ✗ |
| 13 | Herd | `TendTheHerd` (`:38`) | înainte de rații ✗ |
| 14–17 | Industry (arme, fier, piatră, lemn) | `Dig` (`:35`) primul; `ForgeWeapons` (`:43`) după taxe | minerit **înaintea** armelor ✗ |
| 18 | Castle | `RaiseFortification` (`:44`) | după arme ✓, dar înainte de fericire ✗ |
| 19 | Labour | — (alocarea jucătorului rămâne; nu se realocă de la zero) | LIPSĂ |
| 20 | Migration | în `MovePeople` (`:46`), fără vecini | împreună cu 21 ✗ |
| 21 | Population | `MovePeople` (`:46`) | ultimul pas economic ✓ relativ |
| 22 | Merchants | — | LIPSĂ |
| 23 | Muster | — (`Soldiers` e calculat la cerere, `ProvinceEconomy.cs:504`) | n/a |
| 24 | Events expire | `Quieten` (`EventEngine.cs:376`) = cooldown pe tip, nu expirarea modificatorilor | DIFERIT |
| 25 | Labour din nou | `FitWorkforce` (`TurnManager.cs:815`) doar **taie** surplusul; nu realocă | DIFERIT |
| 26 | History | `Remember` (`TurnManager.cs:633`) = media fericirii pe an (`float`), fără populație și fără inel de 400 | DIFERIT |
| 27 | Panels refresh | la cerere, prin `Preview` | echivalent ✓ |

Extra la noi, fără corespondent în listă: `Mercenaries.Season` (`TurnManager.cs:809`, doar la jucător),
`Market.Turned` (`:865`, după ceas).

- [ ] DIFERIT — `EconomySimulation.cs:35-46` + `TurnManager.cs:767-865` — Ordinea e scrisă ca apeluri pe două niveluri: jumătate în `RunTurn`, jumătate în bucla din `AdvanceTurn`, cu pași intercalați per comitat. Nu există o listă explicită de pași, iar ordinea diferă de listă în punctele marcate ✗ mai sus.
- [ ] DIFERIT — `EconomySimulation.cs:523` `ForgeWeapons`, `BlacksmithPage.cs:66-75` — Fierul și lemnul se plătesc **integral la plasarea comenzii**, din UI, apoi fierarul doar numără ture. Efectul („fierul de azi nu intră în armele de azi”) iese la fel, pentru că comanda se plătește din stocul de dinainte de `Dig`. Mecanismul e altul însă: fierarul nu e un job din pipeline și nu consumă nimic la pas.
- [ ] LIPSĂ — Nu există scor. Condiția „nu e în pipeline” e îndeplinită doar pentru că scorul nu există deloc.

## Taxe și tezaur

Comparație la 1.000 de oameni:

| | fără castel, rată 5 | fără castel, rată 50 | cel mai bun castel, rată 5 |
| --- | --- | --- | --- |
| Original (`Pct(Pct(pop, base), rate)`) | 160 | 1.600 | 400 (Royal, 800) |
| Noi (`pop × 0,03 × rate × (1 + bonus)`) | 150 | — (plafon 40 → 1.200) | 183 (treapta 7, +22%) |

- [ ] DIFERIT — `EconomySimulation.cs:344` `TaxDue`, `GameBalance.cs:84` (`GoldPerHeadPerTaxPoint = 0.03`), `data/fortifications.json` (`"tax"`: 0,00…0,22) — La noi e un singur `Mathf.RoundToInt(pop * 0.03f * rate * (1 + bonus))` pe `float`, fără rotunjirea în doi pași. Baza fără castel e 3,0 coroane/om la 100%, nu 3,2. Castelul **adaugă** un bonus mic, de la +0% la +22%, pe o scară de 7 trepte. Originalul **înlocuiește** baza, cu 5 castele de la ×1,50 la ×2,50. În plus, la noi un comitat asediat nu plătește (`:366`), regulă care nu e în listă.
- [ ] DIFERIT — `GameBalance.cs:118` (`MostTaxPercent = 40`), `UI/TaxPanel.cs:122` — Plafonul e 40, nu 50. Minimul 0 e la fel. Blocajul „la fericire 0 rata nu mai poate crește” nu există. Clamp-ul stă în UI, nu în reguli.
- [ ] DIFERIT — `EconomySimulation.cs:351` `TaxGoodwill`, `:448` — Termenul local e `min((5 − rate) × 1, 5)`. Cu rata ≥ 0, plafonul de 5 nu mușcă niciodată, deci **valoarea iese identică** cu `5 − rate`, doar că e `float`. Diferența vine din `empireTerm`: nu se adună aici, ci separat, după pasul de fericire (`TurnManager.cs:795-796`).
- [ ] DIFERIT — `TurnManager.cs:652` `Resented`, `EconomySimulation.cs:356` `TaxSpill`, `GameBalance.cs:139` (`OtherCountiesTaxShare = 0.2`) — La noi e liniar, `−max(0, rate − 5) × 0,2` pentru fiecare **alt** comitat, adică 0 până la rata 5 și apoi −0,2 pe punct. Originalul folosește un tabel pe trepte: 0 până la rata 19, −1 la 20–23… −15 la 50, sumat peste toate comitatele regatului.
- [ ] DIFERIT — `TurnManager.cs:654` — Nu avem overflow (e `float`) și clamp-ul 0–100 se aplică fericirii totale (`:796`). Lista cere însă `int`, cu clamp pe termen.
- [ ] DIFERIT — `EconomySimulation.cs:344` (`Fortifications.TaxBonus(p.Fortification)`), `UI/FortificationsPage.cs:803-814` — Pe durata construcției contează doar castelul existent. La upgrade iese la fel ca `min(existent, în lucru)`. La downgrade nu: UI-ul permite orice treaptă diferită de cea curentă, iar la noi se plătește pe castelul mai mare până la final. [D]
- [ ] LIPSĂ — Nu există `neutralPurse`. Comitatele neutre nu colectează și nu cumpără nimic (`TurnManager._unheld`).
- [ ] DIFERIT — `EventEngine.cs:260-265` („bandits”) — Evenimentul nostru ia 20% din aur (`BanditGoldLoss`) și din grâne, oricând. Originalul **anulează taxa sezonului** și doar dacă taxa afișată e ≥ 10. Nu există un `taxShown` salvat pe care să-l poată citi.
- [x] OK — `EconomySimulation.cs:344` (preview în `TaxPanel` pe populația de acum), `:361` (colectarea după `Eat`, pe populația din acel moment).
- [ ] DIFERIT — `Treasury.cs:5`, `ProvinceEconomy.cs:63-67`, `:133` (`Armoury`) — Doar **aurul** e pe regat (`Treasury`, pus în comun de `TurnManager.PoolPurses`). Fierul, piatra, lemnul și armele sunt **per comitat** („stores stay where they were reaped”, cum scrie în CLAUDE.md). Lista le cere globale, cu hrana locală. La noi hrana e locală, ca în original.

## Hrană și rații

Comparație la 1.000 de oameni, rație Normal:

| | necesar | oameni/unitate |
| --- | --- | --- |
| Original | 1.000 porții → saci `DivCeil(·, 6)` / vite `DivCeil(·, 10)` / lactate 5 pe vacă | sac 6 · vită tăiată 10 · lactate 5/vacă |
| Noi | `Ceil(1000/10 × 1,0)` = 100 „grâne” | sac 10 · vită tăiată 40 (`BeefPerCow = 4` × 10) · lactate 6/vacă × acoperire îngrijitori × sezon |

- [ ] DIFERIT — `EconomySimulation.cs:252`, `EconomyEnums.cs` `RationLevel`, `GameBalance.cs:421` — Necesarul e `Ceil(pop / 10 × mult)` pe `float`, unde 1 sac hrănește 10 oameni, nu 6. Avem **5 trepte** (None, Half, Normal, Double, Triple): **lipsește Quarter**. Nici rotunjirea nu e `DivCeil(pop, divisor) × multiplier`.
- [ ] DIFERIT — `EconomySimulation.cs:230`, `:259` — Lactatele se scad primele și fără să omoare vite, ca în original. Cantitatea însă e `floor(vite × 0,6 × acoperireÎngrijitori × multiplicatorSezon)` unități de grâne, adică ~6 oameni/vacă, redusă de lipsa îngrijitorilor și de sezon. Originalul dă fix `herd × 5` oameni.
- [ ] DIFERIT — `EconomySimulation.cs:263-265`, `ProvinceEconomy.cs:44` (`BeefShare`) — Split-ul procentual există (`BeefShare` 0–100), dar în unități de grâne: vite = `Ceil(parte / 4)` (40 oameni/cap), saci = parte (10 oameni/sac). Originalul cere vite = `DivCeil(oameni, 10)` și saci = `DivCeil(oameni, 6)`.
- [ ] DIFERIT — `EconomySimulation.cs:270-285` — Split-ul e ponderat, dar după el **golul unei părți e acoperit de cealaltă**: întâi grâne, apoi vite. Asta e exact coada de priorități pe care lista o exclude.
- [ ] DIFERIT — `EconomySimulation.cs:264-265`, `:303`, `:327` `Achieved` — Nu există bucla care coboară treapta de la `wanted` până încape. La noi se **consumă tot ce se poate din necesarul treptei cerute**, iar treapta obținută se deduce apoi din multiplul servit. Efect: o rație Double care nu încape golește grânarul și obține Normal. În original s-ar consuma doar necesarul treptei care încape.
- [ ] DIFERIT — `ProvinceEconomy.cs:38` (`Ration`, salvat), `TurnSummary.cs:29` (`Achieved`) — `rationAchieved` stă doar în `TurnSummary`, iar `_lastSeason` nu se salvează (vezi Arhitectură). După load, treapta obținută se pierde.
- [ ] DIFERIT — `EconomySimulation.cs:251`, `ProvinceEconomy.cs:376` (`Fed`), `TurnManager.cs:500` — Soldații mănâncă mereu (nu există opțiunea Armies Eat), cu un apetit fix de ×1,6 (`GameBalance.cs:274`), nu la rația comitatului. Mănâncă din comitatul **care i-a ridicat** (`Home`), nu din cel în care stau. Trupele inamice nu mănâncă. Garnizoana din castel mănâncă din stocuri, iar sub asediu din `CastleStores`. Originalul are soldații prezenți (prieteni și dușmani) la rația comitatului și garnizoana **în afara** stocurilor.
- [ ] DIFERIT — `UI/RationPanel.cs:101-103` — Slider-ul are pas fix de 5 pe 0–100 și nu sare peste valorile care nu schimbă nimic.
- [ ] LIPSĂ — Nu există transport de grâne sau vite cu căruțe între comitate.
- [x] OK — `EconomyCheck.cs:452-463` — Testul există: Double, 0 vite, grâne pentru o singură rație Normal → `Achieved == Normal`, deci rația coboară. Exploit-ul nu e copiat.

Extra la noi, fără corespondent în listă: **foametea separată** (`EconomySimulation.cs:312-321`). Când
porțiile nu acoperă o rație Normal, moare până la 7,5% din populație × deficit și fericirea scade cu
până la 25. În original foamea lucrează doar prin treapta None: fericire −8, sănătate −8…−20, iar de
acolo mortalitate.

## Sănătate

Nu există niciun contor de sănătate: niciun câmp în `ProvinceEconomy`, niciun termen în
`SettleLoyalty`, nicio bandă.

- [ ] LIPSĂ — Tabelul `[rație][bandă]` care mișcă sănătatea.
- [ ] LIPSĂ — Contorul 0–100 și cele 5 benzi (Diseased…Perfect).
- [ ] LIPSĂ — Fericirea din bandă (−10, −5, 0, +1, +2).
- [ ] LIPSĂ — Scăderea din Perfect sub orice rație mai mică decât Double.
- [ ] DIFERIT — `EventEngine.cs:186-190` — Ciuma există, dar ca eveniment de mai multe sezoane care ia `PlagueDeathRate` din populație la fiecare sezon (plus fericire, `PlagueLoyaltyLoss`). Nu atinge sănătatea, care nu există. Formula originalului e verificată la „Evenimente random”.

## Fericire

La noi fericirea se numește `Loyalty` și e `float` (`ProvinceEconomy.cs:19`). Termenii ei, puși lângă
cei din original:

| Termen | Original | Noi |
| --- | --- | --- |
| taxă locală | `5 − rate` | `min(5 − rate, 5)`, deci aceeași valoare (`EconomySimulation.cs:351`) |
| taxă imperiu | tabel pe trepte, în pasul Tax | liniar, după pasul de fericire (`TurnManager.cs:795`) |
| sănătate | −10…+2 | — |
| rație (None…Triple) | −8, −5, −2, +1, +4, +7 | −15, —, −6, 0, +3, +5 (`GameBalance.cs:431`) |
| foamete | — | până la −25 (`EconomySimulation.cs:320`) |
| recrutare | imediat, pe procent | pe sezon, `−recrutațiRecent/100 × 7`, uitat 34%/sezon (`:450`, `:458`) |
| garnizoană încartiruită | — | peste 5% din populație (`:451`, `Billeted`) |
| evenimente | ciumă etc. | `EventEngine.cs:349` |
| cucerire | „scade puternic” | `ConquestResentment` (`TurnManager.cs:354`) |

**Pasul de sezon**

- [ ] DIFERIT — `EconomySimulation.cs:446-460` `SettleLoyalty` — La noi se adună taxă + rație + recrutare + garnizoană. Foametea e aplicată înainte (`:321`), vecinii după (`TurnManager.cs:796`), iar evenimentele și mai târziu (`EventEngine.cs:349`). Lipsește termenul de sănătate, iar valorile rației diferă (tabelul de mai sus). `happinessLast` există doar ca `TurnSummary.LoyaltyBefore`, care nu se salvează.
- [ ] LIPSĂ — Comitatul AI cu 0 populație nu primește fericire 50.
- [ ] LIPSĂ — Neutrul sub 75 nu primește +5, pentru că neutrii nu sunt simulați.
- [ ] DIFERIT — `EconomySimulation.cs:453` (clamp 0–100 ✓), `TurnManager.cs:633` `Remember`, `ProvinceEconomy.cs:495` — Clamp-ul e la fel. În loc de „suma cumulată + media pe tot jocul” ținem media pe **fiecare an** (`HappinessByYear`, `float`), fără suma pe joc.
- [ ] DIFERIT — `EconomySimulation.cs:21` — `TurnSummary` e nou în fiecare sezon, deci termenii afișați se resetează. Termenul „ale” însă nu există, iar „army” nu e costul imediat al recrutării, ci o penalizare care se stinge pe parcursul a mai multe sezoane.
- [ ] DIFERIT — Nu există testul de echilibru. La noi, fără sănătate și cu rație Normal = 0, fericirea stă pe loc la **taxă 5**. În original stă pe loc la taxă 8 (Perfect) sau 7 (Good).

**Berea**

- [ ] LIPSĂ — Nu există bere (niciun `aleGiven`, niciun buton de cheltuit coroane pe fericire).
- [ ] LIPSĂ — Plafonul `5 − aleGiven`.
- [ ] LIPSĂ — Resetarea `aleGiven` pe sezon.

**Recrutarea**

- [ ] DIFERIT — `EconomySimulation.cs:576-579` `Conscript`, `:450`, `GameBalance.cs:319-320` — Costul se calculează pe **numărul absolut** de oameni (7 puncte la 100), nu pe procentul din populație, și nu se aplică imediat: se întinde pe sezoane și scade cu 34% pe sezon. Tabelul 5%→2 … 60%+→101 nu există.
- [ ] DIFERIT — `EconomySimulation.cs:453` — Clamp-ul la 0 există, dar costul vine la sfârșitul sezonului, nu la momentul recrutării.
- [ ] DIFERIT — Bug-ul „sub 50 de oameni e gratis” nu există la noi, pentru că nu avem tabel. Recrutarea într-un comitat mic rămâne totuși aproape gratuită: 49 din 50 de oameni costă ~3,4 puncte. Originalul corectat ar costa 101.
- [ ] LIPSĂ — `UI/RecruitsPage.cs:160` — Nimic nu blochează recrutarea la fericire 0.

## Populație și migrație

La noi totul stă în `EconomySimulation.cs:484` `MovePeople`, cu constantele din `GameBalance.cs:79`
și `:97-105`:

```
dacă nu e foamete și fericirea > 35:  pop += Round(pop × 1% × RationGrowthMultiplier[rație])   // 0; 0,5; 1; 1,5; 2
dacă fericirea ≥ 72:                  pop += Round(pop × 0,6%)                                // imigranți din neant
altfel dacă fericirea ≤ 35:           pop -= Round(pop × 1,2% × (1 + 2 × adâncime))            // emigranți spre nicăieri
```

**Nașteri și decese**

- [ ] DIFERIT — `EconomySimulation.cs:484-520` — Nu e pseudocodul listei: nu există pas de decese, nici `+1` minim la nașteri sau decese, nici `+2` la Diseased, nici ajustarea `rate < deathR`. Populația scade doar prin foamete (`:319`), ciumă (`EventEngine.cs:189`), emigrare și recrutare.
- [ ] DIFERIT — `GameBalance.cs:79`, `:425` — Natalitatea e fix 1%/sezon × multiplicatorul rației. Nu există scara pe mărimea populației (100% la ≤40 … 1% la ≤3000) și nici factorul pe fericire (25/50/75/100/120).
- [ ] LIPSĂ — Mortalitatea pe bandă de sănătate (sănătatea nu există).
- [ ] LIPSĂ — Mortalitatea pe sezon (4/0/2/8%).

**Migrația**

- [ ] DIFERIT — `EconomySimulation.cs:500-519`, `GameBalance.cs:97-105` — Migrația e pe praguri absolute, nu față de cel mai fericit vecin: peste 72 apar oameni din neant (0,6%), sub 35 dispar (1,2%, mai mult cu cât fericirea e mai jos). **Populația nu se conservă între comitate.** Nu există `Pct(best − hap, (100 − hap)/3)` și nici plafonul de 100 de oameni.
- [ ] LIPSĂ — `movers /= 2` pentru neutri.
- [ ] DIFERIT — La fericire 100 nu emigrează nimeni, ca în original, dar diferențele dintre vecini nu contează deloc: 72 lângă 77 primește imigranți doar pentru că e ≥ 72.
- [ ] LIPSĂ — Nu există listă de vecini. `provinces.json` și `ProvinceDefinition` nu au adiacență, iar harta știe doar de granițe desenate (`MarchGrid`).
- [x] OK — Bug-ul din lista surselor de imigranți nu există la noi, pentru că nu există nici panoul care să-l arate.

## Muncă: 9 joburi și alocatorul

Rescris după checklist în `Labour.cs` (2026-09-24).

- [x] OK — 9 joburi: grâne, vite, recuperare (`ReclaimWorkers`), castel, fier, piatră, lemn, fierar (`SmithWorkers`), Idle = restul.
- [x] OK — `IndustryShare` (start 25%) + procente salvate pe fiecare jumătate (`Shares`); fiecare job primește `Pct(jumătate, share)` sau plafonul; resturile se plimbă în jumătate (grâne și vite 5 ture la una a recuperării); restul e Idle.
- [x] OK — Realocare de la zero de 2 ori pe sezon (`RunTurn` după castel și după populație) + la sfârșitul sezonului în `TurnManager`, după bară, după on/off și după comenzile fierăriei.
- [x] OK — Plafon util: fier/piatră/lemn `Bottomless` (100.000) dacă există și sunt pornite, 0 altfel; grâne și vite după cererea sezonului.
- [x] OK — On/off pe fier, piatră, lemn, fierar (`Shut`); a cere oameni la un sit oprit îl pornește.
- [x] OK — Mutarea unei figuri rescrie procentele (`Labour.Ask`).
- [x] OK — Eficiență per sit: 15% la start, crește compus cât are măcar un om, revine la 15% la zero. Rata de creștere (`SiteEfficiencyGrowth`) e presupusă [I].
- [x] OK — Invariant testat pe 8 sezoane: fiecare om pe un singur job, niciun job peste plafon.
- [x] OK — O iconiță = `ceil(pop/25)` oameni (`WorkerFigures`), figuri fantomă sub prag.
- [ ] DIFERIT — Plafonul grânelor și al vitelor e cererea sezonului din modelul nostru, nu căutarea pe `workers = 0…pop` (modelul de grâne și de vite e încă al nostru, vezi secțiunile lor).
- [ ] DIFERIT [I] — Zidarii pot lucra din primul sezon: materialele se plătesc la comandă.
- [ ] DIFERIT [I] — Procentele de start pe fiecare jumătate nu sunt cunoscute (`Labour.Opening`). Siturile unui comitat nou încep la 100% eficiență, nu la 15%.
- [ ] DIFERIT — Figurile se mută cu −/+, nu prin drag.

## Câmpuri, grâne, fertilitate, vreme

Câmpurile noastre sunt un vector `FieldUse[] Fields` (Fallow, Grain, Pasture) cu o fertilitate `float` 0…1 pe
fiecare câmp (`ProvinceEconomy.cs:433-434`). Nu există tile-uri, deșert sau vreme. Anul grânelor la noi:

| Sezon (la noi) | Ce face | Unde |
| --- | --- | --- |
| Spring | seamănă `min(stoc, floor(câmpuri × 5 × acoperire))` saci; `StandingCrop = saci` | `EconomySimulation.cs:153-158` |
| Summer | `StandingCrop × lerp(0,45; 1; acoperire)` | `:160-162` |
| Autumn | recoltă `StandingCrop × 24 × GrainModifier × fertilitateMedie × acoperire`, direct în stoc; apoi `RestTheLand` | `:164-170` |
| Winter | nimic | `:172` |

**Câmpuri**

- [ ] DIFERIT — `ProvinceDefinition.cs:17` (`Fields = 10`), `ProvinceEconomy.cs:433`, `:446` `FieldsUnder` — Numărul de câmpuri vine din definiție (implicit 10, Valmere 9) și nu are plafonul de 20. Contoarele se numără la cerere din vector. Nu sunt un cache recalculat din tile-urile hărții și nu au categoriile deșert și în-recuperare.
- [ ] DIFERIT — `UI/CampaignMapPage.cs:355`, `UI/FieldPanel.cs:53`, `EconomySimulation.cs:676` `SetField` — Un câmp se vopsește prin click pe hartă, doar în fallow, grâne sau pășune. Nu există deșert, deci nici meniurile „începe recuperarea” sau „abandonează”, nici regula „câmp lovit de vreme = fără meniu”.
- [ ] DIFERIT — `ProvinceDefinition.cs:21-22` (`InitialGrainFields = 4`, `InitialPastureFields = 3`), `ProvinceEconomy.cs:614-616` — Comitatele încep cu 4 câmpuri de grâne, nu cu 0.
- [ ] DIFERIT — `ProvinceEconomy.cs:444` (`FieldRepair`), `GameBalance.cs:232` (`FieldRepairWork = 400`), `EconomySimulation.cs:193` — Recuperarea e un singur contor pe comitat, cu 400 om-sezoane după o inundație. Scade cu toți grânarii în plus, fără plafonul de 200 pe câmp și sezon. Nu există progres 0–800 pe câmp, nici ordinea „întâi cel mai avansat”.
- [ ] DIFERIT — `EventEngine.cs:213-236` — Inundația șterge cultura, pune `FieldRepair` și scade fertilitatea câmpurilor de grâne. Seceta înjumătățește cultura. Niciuna nu transformă vreun câmp în deșert.

**Ciclul grânelor**

- [ ] DIFERIT — `EconomySimulation.cs:153-158`, `GameBalance.cs:28` (`SeedPerField = 5`) — Semănatul are loc la sfârșitul primăverii (vezi Calendarul), cu 5 saci/câmp înmulțiți cu acoperirea muncii și plafonați la stoc. Nu există căutarea 10…1 saci/câmp cu condițiile de stoc și muncă (`div` 5/2). `crop` = saci, nu `saci × 12`.
- [ ] LIPSĂ — Fallback-ul „cel mult 10 saci pe tot comitatul” nu există.
- [ ] DIFERIT — `EconomySimulation.cs:160-162`, `GameBalance.cs:52` (`UntendedCropYield = 0.45`) — Cultura trece printr-un singur pas de creștere (vara), și acela e doar o penalizare pentru lipsa muncii: `× lerp(0,45; 1; acoperire)`. Nu există `min(crop, muncă × 10 / × 2)` și nici creșterea `+Pct(crop, fertilitate/2)`.
- [ ] DIFERIT — `EconomySimulation.cs:164-167`, `GameBalance.cs:29` (`HarvestPerSeed = 24`) — Recolta e `crop × 24 × GrainModifier × fertilitate × acoperire` (`float`, `RoundToInt`) și intră în stoc chiar în pasul Autumn. Nu e `min(crop, (muncă/2) × 3)` și nici `muncă × 2`.
- [ ] DIFERIT — `EconomySimulation.cs:684-688` — Un câmp de grâne pierdut taie **pe loc** `StandingCrop / câmpuri` din cultură, iar la recoltă efectul e același cu scalarea `câmpuriAcum / câmpuriSemănate`. Un câmp nou nu produce, dar crește necesarul de muncă (`GrainWork`, `:77`) și coboară fertilitatea medie (`ProvinceEconomy.cs:462`). Așa reduce recolta anului, lucru care în original nu se întâmplă.
- [x] OK — Bug-ul „Sunny = 3/2 din tot, oricâți secerători” nu există, pentru că nu avem vreme.

**Fertilitate**

- [ ] DIFERIT — `EconomySimulation.cs:208-222` `RestTheLand`, `GameBalance.cs:57-59` — Fertilitatea e pe **câmp**, `float` 0…1, cu podea 0,35. Se mișcă o dată pe an, după recoltă: grâne −0,12, fallow +0,25, pășune +0,125. Nu e contorul `+6 × fallow − 3 × grâne` pe comitat, clamp −100…+100. E mereu activă (nu există opțiunea AF).
- [ ] DIFERIT — `EconomySimulation.cs:215` — Break-even-ul e la ~1 fallow pentru 2 câmpuri de grâne (0,25 față de 2 × 0,12), apropiat de original. Pășunile însă **contează** (jumătate din fallow), iar în original nu.
- [ ] DIFERIT [I] — `UI/FieldPanel.cs:103` — Nu avem cele 7 etichete. Afișăm „Heart of the land” ca procent pe câmp. Item nesigur, nenumărat ca greșeală.

**Vremea**

- [ ] LIPSĂ — Acumulatorul de uscăciune pe comitat (+8/+24/+12/−12, jitter `(rnd & 0x7F)/8`).
- [ ] LIPSĂ — Comitatul random cu vecini și modificatorul de climă pe bandă.
- [ ] LIPSĂ — Benzile Flooding…Drought și regulile pentru Frost.
- [ ] LIPSĂ — „Fără Advanced Farming vremea e mereu Cloudy”: nu există nici vremea, nici opțiunea.
- [ ] DIFERIT — `EventEngine.cs:213-236` — Inundația (doar primăvara) și seceta (doar vara) sunt evenimente random ponderate (`FloodWeight`, `DroughtWeight`, `GameBalance.cs:202-203`), nu benzi de vreme, și nu strică niciun câmp în deșert.
- [ ] DIFERIT — `EventEngine.cs:219-224`, `:236`, `GameBalance.cs:225`, `:233` — Efectele pe grâne sunt: inundația face deșert un câmp de grâne (cel mai sleit, `EconomySimulation.Flood`) și ia doar cultura lui, cu 400 om-sezon de recuperare pe câmp; seceta ia 50% din cultură și nu strică niciun câmp. Nu există tabelul pe semănat, creștere și recoltă (Flooding ¼, Frost/Storms ½, Sunny ×3/2, Drought ½). [D]

## Vite

La noi (`EconomySimulation.cs:227-236` `TendTheHerd`):

```
acoperire = min(1, îngrijitori / Ceil(vite / 2,5))
spațiu    = pășuni × 20
aglomerare = clamp(2 − vite / spațiu, 0, 1)        // 0 fără pășune
vite     += Round(vite × 5% × CattleModifier × aglomerare × acoperire)
```

Nu există mortalitate: vitele scad doar prin tăiere (`Eat`) și evenimente (murrain).

- [ ] DIFERIT — `EconomySimulation.cs:232-234`, `GameBalance.cs:63`, `:68` — În loc de tabelul pe 4 trepte (nașteri 14/9/5/2%, decese 1/3/5/7%, la 10.000 pe `herd × 100`) avem o singură rată de creștere de 5%, redusă liniar de la 20 la 40 de vite/pășune. Nu există rată de deces.
- [ ] DIFERIT — `EconomySimulation.cs:229`, `:66`, `GameBalance.cs:69` (`CowsPerHerder = 2.5`) — Personalul e `îngrijitori / Ceil(vite / 2,5)`, plafonat la 100%, și reduce creșterea și lactatele. Lipsa lui nu adaugă mortalitate (~34% la 0 îngrijitori) și nu aduce beneficiu peste 100% (până la 200%).
- [ ] DIFERIT — `EconomySimulation.cs:233` — Fără pășune, aglomerarea e 0, deci cireada nu crește, dar nici nu pierde nimic. Originalul pierde jumătate din cireadă, sau toată sub 6 capete.
- [ ] LIPSĂ — Bonusul de natalitate pentru cirezi mici (treptele 5/10/25; decizia B98 rămâne deschisă).
- [ ] DIFERIT — `GameBalance.cs:90` (`CattleSeasonMultiplier = {1,0; 1,1; 1,1; 0,8}`), `EconomySimulation.cs:230` — Multiplicatorul sezonier se aplică doar **lactatelor**. Nu există primăvara ×1,5 la nașteri și nici iarna ×1,5 la decese.
- [ ] LIPSĂ — Efectul vremii pe vite (Frost −2%, Drought −10%, Sunny +5%, Storms −5%, Flooding −10%).
- [ ] DIFERIT — `EconomySimulation.cs:234` — Nașterile se calculează cu `Mathf.RoundToInt(float)`, nu cu împărțirea întreagă `herd × rate / 10000`.
- [ ] DIFERIT — `EconomySimulation.cs:66` — Plafonul de îngrijitori e fix, `Ceil(vite / 2,5)`. Nu e căutarea care maximizează `nașteri − decese` și nu are plafonul absolut de 6/cap.
- [x] OK — `UI/ProvinceSidebar.cs:194` — Forecast-ul e `Preview` (`RunTurn` pe copie), deci creștere minus tăiere, fără evenimente, care rulează doar în `TurnManager`. Vreme nu există. Termenii înșiși diferă (vezi mai sus).
- [ ] LIPSĂ — `Market.cs` — Cumpărarea de vite nu transformă niciun câmp în pășune. [D]

## Industrie și castele

**Industrie**

- [ ] DIFERIT — `EconomySimulation.cs:133` `Dig`, `ProvinceDefinition.cs:26-28`, `UI/BlacksmithPage.cs:72-77` — Trei industrii (lemn, piatră, fier) exprimate prin capacitatea de muncitori din definiție. Fierarul e o pagină de comenzi, nu un sit cu muncitori. Niciuna nu are switch on/off.
- [ ] DIFERIT — `UI/MapDecoration.cs:323-325` `AddSite` — Direcția e inversă: harta **desenează** siturile din capacitățile scrise în `ProvinceDefinition` (`*WorkerCapacity = 0` înseamnă că resursa lipsește, cum e la 3 comitate pentru fier și piatră). Resursele nu vin din plasarea siturilor pe hartă.
- [ ] LIPSĂ — O armată inamică pe un sit nu îl dezactivează.
- [ ] LIPSĂ — Nu există eficiență per sit (15% → 100% compus, revenire la 15% la 0 muncitori). Randamentul e fix.
- [ ] DIFERIT [I] — `EconomySimulation.cs:136-138`, `GameBalance.cs:17-19`, `:91-93` — Producția e `RoundToInt(min(muncitori, capacitate) × randament × modificatorComitat × multiplicatorSezon)`, cu randament lemn 0,375, piatră 0,3, fier 0,25 și iarna ×0,7–0,8. Fără eficiență, pe `float`. Diferență notată, nenumărată ca greșeală (item [I]).
- [ ] DIFERIT — `ProvinceEconomy.cs:124` (`Forging`), `UI/BlacksmithPage.cs:59-77` — Un singur tip de armă în lucru per comitat, ca în original. Materialele însă se plătesc integral la comandă, din stocul **comitatului**. Nu există pool global de lemn și fier împărțit între fierarii porniți.
- [ ] DIFERIT [I] — `data/weapons.json` — Costurile la noi sunt pe lot de 10 (cavaleria, lot de 5): arc 25 lemn + 10 aur, arbaletă 30 lemn + 12 fier, sabie 25 fier + 10 lemn, suliță 30 lemn + 8 fier, buzdugan 35 fier + 5 lemn, cal 60 aur + 30 fier + 4 vite. Pe bucată: arc 2,5 lemn față de 0+13, sabie 2,5 fier + 1 lemn față de 10+3 etc. Există și aur și vite în costuri, iar armură de cavaler nu există. Diferență notată, nenumărată ca greșeală (item [I]).
- [ ] DIFERIT — `EconomySimulation.cs:35` (`Dig`), `:43` (`ForgeWeapons`) — Mineritul rulează **înaintea** armelor. Efectul e compensat de plata la comandă (vezi pipeline-ul).

**Castele**

- [ ] DIFERIT — `data/fortifications.json` — Sunt 7 trepte + niciuna (small-palisade … grand-castle), nu 5 + niciunul. Taxa primește un bonus aditiv (+0 … +22%), nu multiplicatorul ×1,50…×2,50.
- [ ] DIFERIT [I] — `data/fortifications.json` — Costuri: palisadă 200 lemn; medium-fort 400 lemn + 60 piatră; large-fort 700 lemn + 150 piatră + 40 fier; small-castle 300 piatră + 120 lemn + 30 fier; medium-castle 600 piatră + 220 lemn + 80 fier; large-castle 1000 piatră + 350 lemn + 160 fier; grand-castle 1500 piatră + 500 lemn + 280 fier. Lista are Palisade 40/400 … Royal 3000/800 (piatră/lemn), fără fier. Diferență notată, nenumărată ca greșeală (item [I]).
- [ ] DIFERIT — `UI/FortificationsPage.cs:801-816`, `EconomySimulation.cs:545` — Materialele se plătesc integral la comandă, din UI, deci zidarii lucrează din primul sezon. Nu există livrare de materiale și nici oprirea construcției: `Building` se golește doar la final (`EconomySimulation.cs:564`).
- [ ] DIFERIT — `UI/FortificationsPage.cs:803-804`, `data/fortifications.json:2` („the cost is the whole thing, not the difference”) — Poți construi orice treaptă diferită de cea curentă, deci și downgrade. Costul e însă mereu prețul întreg al treptei noi: downgrade-ul nu eliberează materiale.
- [ ] LIPSĂ — Castelul nou nu vine cu garnizoană gratuită de arcași scăzută din populație. Garnizoana (`ProvinceEconomy.Castle`) se ia din companii prin `FortificationsPage` (`Muster`/`Mustered`).
- [x] OK — Capacitatea per tip, ca în original (`data/fortifications.json` "garrison"): Palisade 150, Motte & Bailey 200, Norman Keep 200, Stone Castle 400, Royal Castle 600. Între Motte & Bailey și Stone Castle scara urcă din 50 în 50 (Wooden Keep 250, Small Keep 300, Keep 350), deci Keep-ul nostru ține 350, nu 200 cât Norman Keep — decizie de design. Cifrele vin din ghidurile comunității, confirmate de două căutări, dar necitite direct într-un ghid [I].
- [ ] LIPSĂ — Nu există daune la castel și nici reparații după asediu.
- [ ] LIPSĂ — Nu există scor, deci nici regula „castel în construcție nu contează”.
- [ ] LIPSĂ — Nu avem designer de castel. Lista permite omiterea lui în v1.

## Evenimente random

Pachetul nostru (`EventEngine.cs:182-290`, `GameBalance.cs:187-266`) nu e un pachet de 256 de sloturi. La
fiecare comitat deținut, după economia lui:

- O ciumă deja în curs ia `9%` din populație pe sezon, timp de 3 sezoane, și închide rundă.
- Primele 4 ture (`QuietOpeningTurns`) nu se întâmplă nimic.
- Altfel, un singur zar `Randf() < 0,2` decide dacă „lumea se mișcă”.
- Dacă se mișcă, se trage ponderat, peste un plafon minim de 12 (`WorldEventFloor`), dintre cele eligibile:

| Eveniment | Pondere | Condiție | Efect imediat |
| --- | --- | --- | --- |
| plague | 1 (flămând: `PlagueWeightHungry`) | oricând | −9% pop/sezon × 3, −6 fericire |
| flood | 2 | primăvara, cu cultură în picioare | un câmp de grâne → `FieldUse.Waste` (cu cultura lui), 400 om-sezon de recuperare, apoi fallow |
| drought | 2 | vara, cu cultură | −50% cultură |
| rats | 3 | grâne > `RatsGranary` | −25% grâne |
| murrain | 3 | vite > pășuni × `CowsPerField` | −30% vite |
| bandits | 3 | fericire < `UnrestBelow` sau 0 soldați | −20% aur, −grâne |
| bumper | 3 | toamna, recoltă > 0, fertilitate bună | +25% din recoltă, +4 fericire |

Fiecare tip mai are un cooldown de 4 ture (`EventQuietTurns`). Pe lângă ele există „vestea poporului”
(revoltă, foamete, emigrare, neliniște), care nu e random și nu are efect. `data/events.json` are 28 de
texte, dintre care 8 sunt mercenari.

- [ ] DIFERIT — `EventEngine.cs:193`, `:297-335` `Draw`, `GameBalance.cs:187-207` — La noi sunt 7 evenimente, alese de un zar de 20% și apoi o tragere ponderată pe `float`, cu condiții de eligibilitate și cooldown. Nu e pachetul de 256 de sloturi cu 24 de evenimente. Evenimentele rulează după economie (`TurnManager.cs:803`), nu la pasul 2.
- [x] OK — `TurnManager.cs:784-803` — Evenimentele se trag pentru **fiecare** comitat deținut, jucător sau AI. Exceptarea comitatelor cu index par nu e copiată.
- [ ] DIFERIT — `EventEngine.cs:241-275` — Efectele sunt pierderi sau câștiguri **imediate** și definitive pe stoc (rats −25% grâne, murrain −30% vite, bumper +25%). Nu sunt modificatori procentuali pe pasul sezonului, șterși la „Events expire”. `Quieten` (`:376`) e doar cooldown pe tip.
- [ ] DIFERIT — `EventEngine.cs:186-190`, `:203-211`, `GameBalance.cs:219-220` — Ciuma ia 9% din populație pe sezon, 3 sezoane la rând, plus −6 fericire, oricare ar fi sezonul. Originalul face +20/30/30/40% din decesele sezonului + 10, plafonat la 20% din comitat, și lovește și sănătatea, care la noi nu există.
- [ ] LIPSĂ — Nu există wedding fever.
- [x] OK — `EventEngine.cs:189`, `:208` — Singurul eveniment care atinge `Population` e ciuma. Restul mișcă doar stocuri, cultură sau fericire. Wedding fever lipsește (vezi mai sus).
- [ ] DIFERIT — `EventEngine.cs:258-266` — „Bandits” ia 20% din aur și din grâne. Originalul anulează taxa sezonului și doar dacă taxa afișată e ≥ 10.
- [ ] DIFERIT — `TurnManager.cs:63` (`News`), `UI/CampaignMapPage.cs:1182` (`_advisor.Tell`), `SaveGame.cs` — Veștile se arată imediat după tură și se înlocuiesc la fiecare tură. Nu există flag „scrisoare necitită” care să aștepte afișarea, iar `News` nu se salvează.
- [x] OK — `data/events.json` — Textele sunt ale noastre (de exemplu `rats-01`: „My lord, forgive your stewards…”), cu voce proprie în `assets/audio/events`.

## Revoltă, secesiune, faliment

**Revolta**

Nu există contorul de revoltă. „Revolta” și „neliniștea” de la noi sunt doar mesaje ale consilierului. Codul spune explicit că nu fac nimic: „None of these does anything” (`EventEngine.cs:79-80`).

- [ ] LIPSĂ — Contorul `unrest` 0–4 pe comitat nu există.
- [ ] DIFERIT — `EventEngine.cs:116-119`, `GameBalance.cs:261` — Omul primește doar mesajul `unrest-<cauză>`, când fericirea e sub 35 (`UnrestBelow`) și a scăzut în sezonul acesta. Pauza dintre mesaje e `EventQuietTurns`. Lipsesc avertismentul la primul sezon sub 30, contorul care urcă sub 25, mesajele 1–4 și resetarea la ≥ 25.
- [ ] LIPSĂ — AI-ul nu are neliniște (≥ 41 reset, 11–40 coboară, sub 1 urcă). Știrile din comitatele AI sunt oricum aruncate (`TurnManager.cs:825-829`).
- [ ] DIFERIT — `EventEngine.cs:92-95` — Mesajul `revolt-<cauză>` apare la fericire ≤ 0 și se repetă după pauză. Nu e legat de contorul ajuns la 4 și nu are niciun efect.
- [ ] LIPSĂ — Nu se desprinde nicio gloată (30% din populație, moral 50) și comitatul nu devine neutru.
- [ ] LIPSĂ — Regula „fără tile liber, contorul rămâne la 4” nu există.
- [ ] LIPSĂ — Gloata nu scade fericirea comitatelor prin care trece și nu există recucerire de la o gloată.

**Secesiunea**

- [ ] LIPSĂ — Un regat nu păstrează doar blocul contiguu cu cea mai mare populație. Nu există nicio verificare de contiguitate. `TurnManager.cs:367` `Refuge` doar mută companiile unui comitat căzut la alt comitat al aceluiași lord.
- [ ] LIPSĂ — Nu există listă de vecini (vezi Populație și migrație).
- [ ] LIPSĂ — Regula de departajare „câștigă blocul găsit ultimul”.
- [ ] LIPSĂ — Mesajul „Your lands divide”.
- [ ] LIPSĂ — Nu există nicio cale prin care un comitat deținut să devină neutru. `Claim` (`TurnManager.cs:294`) mută comitatul doar între regate.

**Falimentul**

- [ ] DIFERIT — `EconomySimulation.cs:374-390` `PayTheGarrison`, `:395` `Disband`, `GameBalance.cs:291-292` — Nu există scara de 6 sezoane. În fiecare sezon neplătit dezertează imediat `partea neplătită × 35%` din fiecare companie și din garnizoană, rotunjit în sus. Mercenarii nu pleacă primii, iar la final armatele nu se desființează toate deodată.
- [ ] LIPSĂ — Nu există scară, deci nici resetarea ei la o plată completă.
- [ ] LIPSĂ — Treptele din `kingdom.md` §7.7 nu sunt implementate (și nici încă citite).

## Comerț: negustori

La noi e o singură piață fixă, `Market.cs`, comună tuturor lorzilor (`TurnManager.cs:59`). Prețul are trei componente: baza, un multiplicator pe sezon și o „presiune” lăsată de tranzacțiile anterioare, care se stinge cu 25% pe sezon. Din el se face o marjă de ±8% între cumpărare și vânzare.

- [ ] LIPSĂ — Nu există cei 6 negustori, rutele pe hartă sau mersul lor pe segmente (faza 6).
- [ ] DIFERIT — `Market.cs:43-57` — Avem 11 bunuri: grâne, vite, lemn, piatră, fier, sabie, arc, arbaletă, suliță, buzdugan, cal. Lipsesc berea și armura de cavaler, iar calul nu e în listă. Oaia și lâna lipsesc, ceea ce e acceptabil după listă.
- [ ] DIFERIT — `Market.cs:82`, `:88`, `:90`, `GameBalance.cs:155-159` — Cumpărarea e `ceil(preț × 1,08)`, vânzarea `floor(preț × 0,92)`, cu prețul `max(1, round(bază × sezon × (1 + presiune)))`, limitat între 0,45 și 2,4. La noi cumpărarea costă cam ×1,17 din vânzare. Originalul cere `bază + moral%`, adică exact dublu, cu minim 1 și cu berea ca excepție.
- [ ] DIFERIT [I] — `GameBalance.cs:143-147`, `:171-176` — Prețurile diferă cu un ordin de mărime. Diferența e notată, dar nu se numără ca greșeală. Nici unitățile nu coincid: sacul nostru hrănește 10 oameni, al lor 6.

| Bun | Listă (vânzare/cumpărare) | Noi (baza → vânzare/cumpărare, fără sezon și presiune) |
| --- | --- | --- |
| vacă | 12/24 | 42 → 38/46 |
| sac grâne | 2/4 | 10 → 9/11 (×0,75–1,30 pe sezon) |
| piatră | 2/4 | 22 → 20/24 |
| fier | 1/2 | 35 → 32/38 |
| lemn | 1/2 | 14 → 12/16 |
| buzdugan | 10/20 | 110 → 101/119 |
| suliță | 13/26 | 60 → 55/65 |
| arc | 16/32 | 70 → 64/76 |
| sabie | 23/46 | 120 → 110/130 |
| arbaletă | 24/48 | 95 → 87/103 |
| armură cavaler | 44/88 | — |
| bere | 1 | — |
| cal | — | 260 → 239/281 |

- [x] OK — `Market.cs:146-158` `Buy`, `:161` `Sell` — Tranzacția se face întreagă sau deloc: returnează `false` fără să schimbe nimic dacă aurul sau stocul nu ajung.
- [ ] LIPSĂ — Comitatele neutre nu cumpără hrană. Nu au pungă și nu sunt simulate.
- [ ] DIFERIT — `Market.cs:155` (`province.Add(store, amount)`) — Tot ce se cumpără intră în comitatul care face tranzacția, adică cel deschis în `MarketPage` (`UI/MarketPage.cs:379`) sau comitatul AI (`LordAI.cs:164`). Nu intră în comitatul negustorului. Resursele și armele rămân pe comitat, nu merg în tezaurul global (vezi Taxe și tezaur).

## Armate pe hartă

**Recrutare și echipare**

- [ ] DIFERIT — `data/recruits.json`, `UI/RecruitsPage.cs:292-345` — Avem aceleași 7 tipuri: `peasant`, `mace`, `spear`, `sword`, `bow`, `crossbow` și `horse` (Cavalry). Pașii recrutare → armurărie → creare armată sunt însă comprimați într-unul singur. `Raise` ia oamenii și arma din `Armoury` și face direct o companie nouă (`:340`). Cavaleria costă 2 oameni + `horse` (`data/weapons.json`: aur 60, fier 30, vite 4), nu 1 om + armură de cavaler.
- [ ] DIFERIT — `EconomySimulation.cs:576` `Conscript`, `UI/RecruitsPage.cs:160` — Recruții se scad din populație, ca în original. Costul în fericire nu vine însă din tabelul pe procent: e penalizarea `ConscriptedRecently`, întinsă pe sezoane (vezi „Fericire”).
- [ ] DIFERIT — `UI/CampaignMapPage.cs:348-349` — La desființare, oamenii se întorc în comitatul **de origine** (`home.Population += army.Strength`), nu în cel unde stă armata. Armele se pierd, nu se întorc în tezaur. Regula stă în UI, nu în simulare.
- [ ] DIFERIT — `UI/SplitPanel.cs:19` (`LeastCompany = 1`), `ProvinceEconomy.cs:203` `Split` — Minimul e 1 om, nu 50, și nu există excepția pentru castel. Există un singur mod de split (număr pe tip de unitate), nu 3.
- [ ] DIFERIT [I] — `TurnManager.cs:200` `Merge` — Merge-ul nu are nicio limită (comunitatea zice 1500). Diferență notată, nenumărată ca greșeală.
- [ ] DIFERIT [I] — `UI/RecruitsPage.cs:335-345` — Mercenarii angajați în același muster intră în **aceeași** companie cu trupele proprii, ca unitatea lor de bază (scoțienii devin `spear`). Diferență notată, nenumărată ca greșeală.

**Mișcare**

- [ ] DIFERIT — `UI/MarchGrid.cs:21`, `:110` (A* cu `Guess`), `UI/CampaignMapPage.cs:1024-1047` — Avem o hartă de cost pe celule de 12 px și A*, nu flood fill + extractor de drum. Nu e partajată cu negustori sau transporturi, care nu există.
- [ ] DIFERIT [I] — `GameBalance.cs:280/285/286` (`MarchReach = 520`, drum 1, în afara drumului 2,2), `UI/CampaignMapPage.cs:1027/1032/1045` — Bugetul e `float`, în unități de pixel-cost, nu 15 tile-uri. Impracticabile sunt apa, pantele abrupte și șanțurile de graniță. Pădurea se poate traversa. Diferență notată, nenumărată ca greșeală.
- [ ] LIPSĂ — Călcarea câmpurilor inamice nu distruge cultura sau vitele. O armată pe un sit industrial nu îl oprește 3 sezoane.
- [x] OK — `TurnManager.cs:169` — Orice `March` reușit apelează `Lift(army)` și ridică asediul.
- [ ] LIPSĂ — Exploration: nu există dezvăluirea 13×13 și nici fog of war.

**Solde și foraging**

- [ ] DIFERIT — `EconomySimulation.cs:374` `PayTheGarrison`, `:388`, `GameBalance.cs:291-292` — Solda se plătește la pasul economic, dar e 0,6 aur/soldat/**sezon** (2,4 pe an), față de ~1/3 pe an la comunitate [I]. Neplata nu pornește scara de faliment: dezertează imediat 35% × cota neplătită.
- [ ] DIFERIT — `ProvinceEconomy.cs:376` `Fed`, `EconomySimulation.cs:251-253` — Armata mănâncă din comitatul **de origine**, oriunde s-ar afla, cu apetit ×1,6, adunat la necesarul comitatului. Nu mănâncă din comitatul în care stă, nu mănâncă înaintea populației, iar trupele ostile nu se servesc singure.
- [ ] LIPSĂ — Nu există regula pentru armata mai mare decât cămara (avertisment, 10% dezertare pe sezon, dizolvare). Foametea lovește doar populația (`EconomySimulation.cs:312-321`).

**Cucerire**

- [x] OK — `UI/CampaignMapPage.cs:659` `Contested`, `MapDecoration.TownAt` — Oprirea pe sediul (orașul) unui comitat străin deschide bătălia. *Notă:* prin `BattlePanel` omul alege între asalt și asediu.
- [ ] DIFERIT — `TurnManager.cs:391` `Attack(…, walls: true)`, `UI/BattlePanel.cs:333` — Un castel cu garnizoană străină se poate lua și **cu asalt direct**, la orice treaptă. Doar lățimea frontului (`frontage`) îl face impracticabil la treptele de sus. Originalul îl permite doar prin asediu.
- [ ] LIPSĂ — `TurnManager.cs:606` — Neutrul cu fericire sub 11 nu se predă fără luptă. Neutrul ridică mereu o miliție de `MilitiaShare` (14%) din populația inițială, iar fericirea nu contează.
- [x] OK — `TurnManager.cs:354`, `GameBalance.cs:362` — Cucerirea scade fericirea cu `ConquestResentment` (20).
- [ ] LIPSĂ — Nu există prizonieri sau răscumpărare (conform deciziei, omise în v1).

**Mercenari**

- [ ] DIFERIT — `data/mercenaries.json` (8 bande), `Mercenaries.cs` `Season`, `TurnManager.cs:809`, `GameBalance.cs:214-215` — Avem 8 bande, nu 12. Nu se plimbă pe hartă: una aleasă aleatoriu apare cu șansă de 12% pe sezon **doar în comitatele jucătorului** și stă 3 sezoane.
- [ ] DIFERIT — `data/mercenaries.json` — Mărimile (50–200), prețurile (1.800–6.000 aur) și statisticile sunt inventate de noi, nu luate din cele 6 tabele din `armies.md` §5.

## Asedii și bătălii

**Autocalc (v1)**

- [ ] DIFERIT — `Battle.cs:100` `Fight`, `:131`, `:179`, `:260`, `GameBalance.cs:333/338/349` — Nu e raportul de putere → scara de 10 trepte → pierderi. La noi e o salvă de deschidere, apoi până la 12 runde de schimburi proporționale (`float`), cu noroc ±15% pe tabără. O tabără care pierde 35% **se rupe** și pierde toată compania (`:154`). E un fel de moral, pe care originalul nu îl are.
- [ ] LIPSĂ — Bătălii AI contra AI nu există, pentru că `LordAI` nu mișcă armate.
- [ ] LIPSĂ — „Will you take the field?” nu există: AI-ul nu atacă niciodată, iar atacul omului se rezolvă pe loc în `BattlePanel`.
- [x] OK — `TurnManager.cs:391-429` — Rezultatul se întoarce în campanie: pierderile se scad din rosters (`Bury`), iar `Claim` mută comitatul. `AttackerWon` înseamnă ce spune, fără inversiunea din original.

**Asediu pe campanie**

- [ ] LIPSĂ — Nu există mașini de asediu (catapultă, turn, berbece) și nici timpul `ceil(muncă / oameni)`.
- [ ] DIFERIT — `TurnManager.cs:440` `Besiege`, `:476` `Sieges`, `GameBalance.cs:356` — Asaltul fără mașini e permis la orice treaptă, iar asediul nu se ridică niciodată pentru lipsă de mașini. Asediul durează până când garnizoana își termină `CastleStores`, flămânzește `SurrenderAfterHungrySeasons` (3) sezoane și se predă.
- [ ] DIFERIT — `Battle.cs:83` `Walls`, `data/fortifications.json` (`defence` 1,20…3,00, `frontage` 70…25) — Castelul valorează `defence × TownDefence (1,15) × moral din fericire`, plus o limită de front pe atacatori. Originalul folosește 160/200/250/320/400% din garnizoană.
- [ ] LIPSĂ — Nu există ulei fierbinte.
- [ ] DIFERIT — `Battle.cs:154`, `TurnManager.cs:417` — Un asalt pierdut **distruge** compania atacatoare, pentru că partea care se rupe e scoasă integral. Originalul pierde doar asediul.

**Bătălia live (după v1)**

- [ ] LIPSĂ — Figuri, unități, formații, max 80 de figuri.
- [ ] LIPSĂ — Melee cu timp de recuperare și lovitură grea.
- [ ] LIPSĂ — Armură contra proiectilelor și proiectile Bresenham.
- [ ] LIPSĂ — AI-ul de bătălie (raport de putere, praguri 5 și 260).
- [ ] LIPSĂ — Sfârșit la 0 oameni sau retragere.
- [ ] LIPSĂ — Asediu live (poarta 20.000 hp, zidul 5.000 hp, ușa donjonului).
- [ ] LIPSĂ — `battle.md` și `battle-ai.md` n-au fost integrate.

## AI și diplomație

**Programul AI, per turn**

- [ ] DIFERIT — `LordAI.cs:33` `TakeTurn` — La noi sunt 6 pași: `Feed` (rația), `Victual` (umple `CastleStores`), `Tax`, `Plough` (un fallow → grâne primăvara), `Work` (`Deploy` + mâini pierdute pe dificultate) și `Trade` (grâne, apoi fier, piatră, lemn pentru aur). Acoperă parțial pașii 3, 4, 5 și 12. Lipsesc recount, inbox, diplomație, castele, garnizoane, armata principală, raiduri, avansul armatelor, arme și industrii, tachinări.

**Personalități**

- [ ] LIPSĂ — `data/campaigns/royal-crown/provinces.json:4-17` — Nu există Knight, Baron, Countess sau Bishop și nici scara de castele pe lord. Un regat are doar `name` și `accent`, iar campania are un singur rival (`northern-watch`).
- [ ] DIFERIT — `EconomyEnums.cs` `Difficulty`, `GameBalance.cs:400-416` — Avem 3 dificultăți (Easy, Medium, Hard), nu 4. Ele schimbă **competența** (`LordIdleHands`, `LordGrainSeasons`, `LordSellsAbove`, `LordTaxFloor`), nu dau aur gratuit pe turn.
- [ ] LIPSĂ — Tabelul mai mic sub 3 comitate și oamenii, vitele și grânele gratuite la 1–4 comitate.
- [ ] LIPSĂ — Compoziția asediului pe lord.
- [ ] LIPSĂ — Rota de arme pe 10 pași pe lord.

**Diplomație**

- [ ] LIPSĂ — Standing −30…+30 pe pereche.
- [ ] LIPSĂ — Cele 7 mesaje, inbox cu 5 sloturi, răspuns în turul următor.
- [ ] LIPSĂ — Cadoul comparat cu maximul trimis vreodată.
- [ ] LIPSĂ — Complimente +15, +8, apoi −4.
- [ ] LIPSĂ — Alianțe exclusive și „grudge”.
- [ ] LIPSĂ — Avertismente, apoi război permanent.
- [ ] DIFERIT — `data/campaigns/royal-crown/provinces.json:4-17` — Culorile sunt fixate per regat în datele campaniei (`accent`), nu alese ca cel mai mic scut liber cu lordul derivat din scut.

## Scor, victorie, campanie, opțiuni

- [ ] LIPSĂ — Nu există `putere = 3 × comitate + armate` și nici eliminare. Un lord fără comitate nu dispare din joc.
- [ ] LIPSĂ — Nu există condiție de victorie. Nici `CampaignMapPage` și nici `TurnManager` nu verifică dacă a mai rămas vreun adversar.
- [ ] LIPSĂ — Nu există scor (castele ×50, % din hartă ×10 etc.).
- [ ] LIPSĂ — Nu există „Greatest noble” la 1270.
- [ ] DIFERIT — `UI/CampaignPage.cs:24-32`, `scripts/Campaign.cs`, `UI/CampaignBriefingPage.cs:28-36`, `EconomyEnums.cs` `Difficulty`, `ProvinceDefinition.cs:36` — Campaniile sunt hărți alese separat, fără lanț. Doar `royal-crown` are date în `data/campaigns/`. Nu există reguli pentru „nu se păstrează nimic” sau „înfrângerea repetă harta”. Dificultatea o alege jucătorul, pe 3 trepte (Easy/Medium/Hard), nu după scara 0,0,1,1,2,2,2,2 pe hărți. Aurul de start e `InitialGold` per comitat, din `.tres` (implicit 400, Kingsreach 800), adunat pe regat. Nu e 5.000/2.500/1.000.
- [ ] DIFERIT — `UI/CampaignBriefingPage.cs:28-36` — La joc nou singura opțiune e dificultatea. Lipsesc aurul, castelul, armurăria, garnizoana, stocurile, numărul de lorzi și starea comitatului de start.

## Goluri și decizii

**Necunoscute chiar și în open-lords2.** Nu sunt reguli de verificat, ci decizii de design pe care le
luăm noi. Mai jos e ce face azi codul nostru pe fiecare:

- [ ] LIPSĂ — Maparea fertilitate → 7 etichete. La noi fertilitatea e un procent 0–100% per câmp (`UI/FieldPanel.cs:103`), fără etichete.
- [ ] DIFERIT — Livrarea materialelor la castel: la noi se plătesc integral la comandă, din comitat (`UI/FortificationsPage.cs:810-812`). Nu există livrare.
- [ ] LIPSĂ — Designer de castel și layout pe câmpul de luptă. Bătălia e autocalc (`Battle.cs`).
- [ ] LIPSĂ — Prizonieri și răscumpărare.
- [ ] LIPSĂ — Umplerea șanțului în asediu.
- [ ] LIPSĂ — Validare pe save-uri târzii (turn 40+). Există doar `LordCheck`, care rulează 40 de ture economice pe un lord AI („a lord who starved his own county in forty turns”, CLAUDE.md), nu pe save-uri reale.

**De citit încă din open-lords2.** Sunt sarcini de documentare, nu de cod. Itemii din audit care trimit
la ele (§7.4 producție, §7.5 castele, §7.7 faliment, §8.1 evenimente, `armies.md`) au fost verificați
doar cât spune checklist-ul. „Identic” pe ei se poate confirma abia după citire.

- [ ] LIPSĂ — `kingdom.md` §7.4, §7.5, §7.6, §7.7, §8.1, §8.2, §14
- [ ] LIPSĂ — `armies.md` (solde, mișcare, autocalc, mercenari)
- [ ] LIPSĂ — `bugs.md`

**Decizii de luat de noi (bug-uri ale originalului).** Stadiul la noi:

- [ ] DIFERIT — Exceptarea comitatelor pare de la evenimente: la noi toate comitatele deținute primesc evenimente (`TurnManager.cs:803`), deci bug-ul **nu e copiat**. Decizia rămâne de confirmat explicit.
- [ ] LIPSĂ — Recolta Sunny care ignoră munca: nu avem vreme, deci decizia se ia la implementare.
- [ ] LIPSĂ — Recrutarea gratuită sub 50 de oameni: nu avem tabelul (vezi Fericire → Recrutarea).
- [ ] LIPSĂ — Overflow-ul `sbyte` la taxa imperiului: nu avem tabelul (vezi Taxe).
- [ ] LIPSĂ — Treptele moarte de aur din scor: nu avem scor.
- [ ] LIPSĂ — Bonusul de natalitate la cirezi mici (B98): nu avem tabelul de vite (vezi Vite).

---

## Ordinea de implementare propusă

Ordinea urmează pipeline-ul de sfârșit de sezon, nu feature-urile. Fiecare pas se construiește pe
starea lăsată de pașii dinaintea lui, deci îl putem testa imediat contra listei (valorile unui sezon
reprodus). Tot ce e **DIFERIT** se rescrie după formula din listă, iar ce e **LIPSĂ** se adaugă.

### Pasul F — fundația (înainte de orice pas, altfel nimic nu poate fi „identic”)

1. **Helper-e întregi** `Pct(x, p)` și `DivCeil(a, b)`, apoi starea de reguli trecută pe `int`: `Loyalty`, `Fertility`, `LabourSplit` și rația ca treaptă, nu ca multiplu.
2. **PRNG propriu**: cele două LFSR-uri de 31 de biți, pășite o dată pe sezon, cu ieșirile `&0x7FFF / &0x7F / &7`. Seed aleatoriu la joc nou, salvat în save. Înlocuiește `RandomNumberGenerator` peste tot, inclusiv în `Battle`.
3. **Calendarul**: start iarna 1268, anul se schimbă la iarnă, iar sezonul se setează la cel **care începe**, înainte de economie.
4. **Pipeline ca listă explicită de pași** (o singură listă, nu `RunTurn` + bucla din `AdvanceTurn`), cu comitatele în ordinea indexului. Iterările pe `Dictionary` din reguli trec pe ordine fixă.
5. **Lista de vecini** per hartă, simetrică, cu test. O cer vremea (3), secesiunea (9a) și migrația (20).
6. **Opțiunile** Advanced Farming, Armies Eat și Exploration ca flag-uri în starea jocului, salvate.
7. **Save complet**: RNG, `rationAchieved`, `happinessLast`, veștile necitite. Plus testul „20 de sezoane, save la 10, reload, sezonul 20 identic”. De aici încolo el e plasa de siguranță pentru toți pașii următori.
8. **Tezaur global** pe regat pentru aur, fier, piatră, lemn și arme, cu hrana locală. Taxele (4), industria (14–17) și castelele (18) depind de el.

Mutarea simulării într-un assembly fără Godot (Arhitectură, item 1) poate merge în paralel: nu schimbă
nicio regulă, doar unde stau ele.

### Pașii pipeline-ului, în ordine

| # | Pas | Ce implementăm (din secțiunile de mai sus) |
| --- | --- | --- |
| 0 | AI fields | după pasul 19. AI-ul folosește alocatorul nou, nu `Deploy` |
| 1 / 1a | Clock, Ledger | din Fundație (3); snapshot-ul există deja (`TurnSummary`) |
| 2 | Events | pachetul de 256 de sloturi, modificatori procentuali pe pași în loc de pierderi imediate, Highwaymen pe `taxShown`, flag „scrisoare necitită” |
| 3 | Weather | acumulatorul de uscăciune, benzile, comitatul random + vecinii, câmpul → deșert, mereu Cloudy fără AF |
| 4 | Tax | `Pct(Pct(pop, base), rate)` cu baza pe castel (320…800), rata 0–50, blocaj la fericire 0, `5 − rate` + `empireTerm` pe tabel (`int` + clamp), minimul castelului în lucru, `neutralPurse` |
| 5 | Wages | formula soldei (`armies.md` §6.4), scara de faliment pe 6 sezoane (§7.7) |
| 6 | Rations | 6 trepte cu Quarter, `DivCeil`, lactate `herd × 5`, split 10/6 oameni, bucla care coboară treapta, `rationWanted/Achieved`, Armies Eat (soldații prezenți, garnizoana în afara stocurilor). Eliminăm foametea separată |
| 7 | Health | contorul 0–100, tabelul `[rație][bandă]`, 5 benzi |
| 8 | Happiness | suma `dHapTax + dHapHealth + dHapRation` pe `int`, AI cu 0 pop → 50, neutru < 75 → +5, suma și media pe joc; în afara pipeline-ului: berea și tabelul de recrutare pe procent, aplicat imediat |
| 9 | Unrest | contorul 0–4 (om și AI), revolta cu gloată (30%, moral 50) pe tile liber în raza 3 |
| 9a | Secession | blocul contiguu cel mai populat pe lista de vecini, restul devin neutre |
| 9b | Field census | câmpurile ca tile-uri pe hartă, cu contoarele cache (fallow/grâne/pășune/deșert/în recuperare), max 20; start cu 0 câmpuri de grâne |
| 10 | Fertility | `+6 × fallow − 3 × grâne` pe comitat, clamp ±100, doar cu AF; maparea pe 7 etichete e decizia noastră [I] |
| 11 | Reclamation | progres 0–800 pe câmp, max 200/câmp/sezon, întâi cel mai avansat |
| 12 | Grain | semănatul 10…1 saci/câmp + fallback, creșterea primăvara și vara, recolta toamna, efectele vremii; decizia pe bug-ul Sunny |
| 13 | Herd | tabelul de aglomerare la 10.000, personal și mortalitate, fără pășune → jumătate, sezonier, vremea, plafonul de îngrijitori prin căutare; decizia B98 |
| 14–17 | Industry | **arme întâi** (fierarul ca job, pool global de fier și lemn), apoi fier, piatră, lemn; situri on/off, eficiența compusă (§7.4) |
| 18 | Castle | 5 castele, livrarea materialelor, apoi munca (plafon 0 până la livrare), garnizoana gratuită de arcași, upgrade/downgrade cu materiale (§7.5) |
| 19 | Labour | alocatorul cu 9 joburi: procente salvate, `usefulCeiling` prin căutare, redistribuirea resturilor, Idle ca job, invariantul testat |
| 20 | Migration | cel mai fericit vecin, `Pct(best − hap, (100 − hap)/3)`, plafon 100, neutru /2 |
| 21 | Population | scara de natalitate × factorul de fericire, mortalitatea pe bandă + sezon, `+1`-urile, ciuma și wedding fever din modificatorii de la pasul 2 |
| 22 | Merchants | cine stă unde; rutele merg cu faza 6 (vezi mai jos) |
| 23 | Muster | recalcularea oamenilor sub arme |
| 24 | Events expire | ștergerea modificatorilor de la pasul 2 |
| 25 | Labour din nou | același alocator ca la 19 |
| 26 | History | inelul de 400 de sezoane × 16 comitate {pop, fericire} |
| 27 | Panels refresh | preview-urile: rații, taxe (`taxShown`) |

### După pipeline: fazele turnului și restul

Ordinea fazelor din turn: **1** neutrii (AI de taxe și câmpuri pe `neutralPurse`) → **2** asediile
mutate înaintea turului jucătorilor, cu mașini și regulile pe castel → **3** transporturile cu căruțe →
**4** programul AI de 14 pași cu personalitățile → **5** gloatele → **6** negustorii pe rute (prețul ×2,
14 bunuri, fără umpleri parțiale) → mișcarea pe tick-uri, cu călcarea câmpurilor și Exploration.

Apoi autocalc-ul pe scara de 10 trepte (asaltul pierdut nu mai distruge armata), eliminarea, victoria și
scorul, diplomația, campania și opțiunile de joc nou. Bătălia live rămâne după v1.
