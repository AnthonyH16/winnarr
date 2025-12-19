# MotoGP Episode Matching Reference

**Purpose:** Document all naming patterns for MotoGP files and TVDB episodes to create a reliable matching dictionary.

**Last Updated:** November 29, 2025

---

## TVDB Naming Formats by Year

### Format A: 2015-2021 (Session-Based, MotoGP class only)
**Pattern:** `R# - Location (Session)`

**Examples:**
- `R1 - Qatar (Free Practice 1)`
- `R1 - Qatar (Qualifying 1)`
- `R1 - Qatar (Qualifying 2)`
- `R1 - Qatar (Warm Up)`
- `R1 - Qatar (Race)`
- `R14 - Aragon (Race)`
- `R16 - Australia (Qualifying 2)`

**Sessions:** Free Practice 1-4, Qualifying 1, Qualifying 2, Warm Up, Race

**Notes:**
- Only MotoGP class episodes (no separate Moto2/Moto3 episodes)
- Round number prefix (R1, R2, etc.)
- Tests use: `Location Official Test (Day #)`

**2020 COVID Special Cases (same circuit, different GP names):**
| TVDB Name | Actual Circuit | Notes |
|-----------|----------------|-------|
| Spain (R2) | Jerez | First Spanish GP |
| Andalucia (R3) | Jerez | Second race at Jerez |
| Austria (R5) | Red Bull Ring | First Austrian GP |
| Styria (R6) | Red Bull Ring | Second Austrian GP |
| San Marino (R7) | Misano | First Misano GP |
| Emilia Romagna (R8) | Misano | Second Misano GP |
| Aragón (R11) | MotorLand Aragón | First Aragon GP |
| Teruel (R12) | MotorLand Aragón | Second Aragon GP |
| Europe (R13) | Valencia | First Valencia GP |
| Valencia (R14) | Valencia | Second Valencia GP |

**File name implications for 2020:**
- "andalucia" or "andalusia" → Andalucia GP (R3)
- "spain" or "jerez" → Could be Spain (R2) or Andalucia (R3)
- "styria" or "steiermark" → Styria GP (R6)
- "austria" → Austria GP (R5)
- "emilia" or "romagna" → Emilia Romagna GP (R8)
- "san marino" or "misano" → Could be San Marino (R7) or Emilia Romagna (R8)
- "teruel" → Teruel GP (R12)
- "aragon" → Could be Aragón (R11) or Teruel (R12)
- "europe" → Europe GP (R13)
- "valencia" → Could be Europe (R13) or Valencia (R14)

**2021 COVID Special Cases (double-headers continued):**
| TVDB Name | Actual Circuit | Notes |
|-----------|----------------|-------|
| Qatar (R1) | Losail | First Qatar GP |
| Doha (R2) | Losail | Second Qatar GP at same circuit |
| Austria (R10) | Red Bull Ring | First Austrian GP |
| Styria (R11) | Red Bull Ring | Second Austrian GP |
| San Marino (R14) | Misano | First Misano GP |
| Emilia Romagna (R16) | Misano | Second Misano GP |
| Algarve (R3) | Portimão | Separate from Portugal GP |
| Portugal (R4) | Portimão | Same circuit as Algarve (run later in season) |

**File name implications for 2021:**
- "qatar" → Qatar GP (R1), but be aware of Doha
- "doha" → Doha GP (R2) at Losail
- "styria" or "steiermark" → Styria GP (R11)
- "austria" → Austria GP (R10)
- "emilia" or "romagna" → Emilia Romagna GP (R16)
- "san marino" or "misano" → Could be San Marino (R14) or Emilia Romagna (R16)
- "algarve" → Algarve GP (R3) at Portimão
- "portimao" or "portugal" → Could be Algarve (R3) or Portugal (R4)

---

### Format B: 2022 (Mixed - Race-Only early season, Race+Qualifying late season)

**Early Season (E01-E33, Qatar through Assen):**
**Pattern:** `Grand Prix of Location (Class) - Circuit Name`

**Examples:**
- `Grand Prix of Qatar (Moto 3) - Losail International Circuit Qatar`
- `Grand Prix of Qatar (Moto 2) - Losail International Circuit Qatar`
- `Grand Prix of Qatar (Moto GP) - Losail International Circuit Qatar`
- `Grand Prix of Indonesia (MotoGP) - Mandalika International Street Circuit`
- `Spanish Grand Prix (Moto2) - Circuit of Jerez - Ángel Nieto`
- `French Grand Prix (MotoGP) - Circuit of Le Mans`
- `Italian Grand Prix (Moto3) - Mugello Circuit`
- `Gran Premi Monster Energy de Catalunya Circuit de Barcelona (MOTOGP)`

**Late Season (E34-E87, British GP through Valencia):**
**Pattern:** `##.Sponsor Location Qualifying/Grand Prix (CLASS)`

**Examples:**
- `12.Monster Energy British Qualifying (MOTO3)`
- `12.Monster Energy British Qualifying (MOTO2)`
- `12.Monster Energy British Qualifying (MOTOGP)`
- `12.Monster Energy British Grand Prix (MOTO3)`
- `12.Monster Energy British Grand Prix (MOTOGP)`
- `13.Motorrad Grand Prix von Österreich Qualifying (MOTO2)` ← Austria!
- `13.Motorrad Grand Prix von Österreich (MOTOGP)` ← Austria race
- `18.Animoca Brands Australian Motorcycle Grand Prix Qualifying (MOTO3)`
- `18.Animoca Brands Australian Motorcycle Grand Prix (MOTOGP)`
- `19.PETRONAS Grand Prix of Malaysia Qualifying (MOTO2)`
- `19.PETRONAS Grand Prix of Malaysia (MOTOGP)`

**Classes:** Moto 3, Moto 2, Moto GP (note spaces), also Moto3, Moto2, MotoGP, MOTO3, MOTO2, MOTOGP

**Notes:**
- Early season: NO session types - each episode IS the race (3 per GP)
- Late season: Has both Qualifying AND Race episodes (6 per GP)
- Round number prefix with dot (12., 13., etc.)
- Various naming: "Grand Prix of X", "X Grand Prix", German "Grand Prix von"
- Austria = "Österreich" in German naming

---

### Format C: 2023 (Full Sessions, All Classes)
**Pattern:** `Grand Prix of Location (Class Session)`

**Examples:**
- `Grand Prix of Portugal (Moto3 Practice 1)`
- `Grand Prix of Portugal (Moto2 Practice 2)`
- `Grand Prix of Portugal (MotoGP Practice 1)`
- `Grand Prix of Portugal (MotoGP Free Practice)`
- `Grand Prix of Portugal (MotoGP Qualifying)`
- `Grand Prix of Portugal (MotoGP Qualifying Nr. 2)`
- `Grand Prix of Portugal (Moto3 Qualifying)`
- `Grand Prix of Portugal (MotoGP Sprint)`
- `Grand Prix of Portugal (Moto3 Race)`
- `Grand Prix of Portugal (MotoGP Race)`
- `Grand Prix of Portugal (MotoGP Warm Up)`

**Sessions:** Practice 1-3, Free Practice, Qualifying, Qualifying Nr. 2, Sprint, Race, Warm Up

**Notes:**
- Class and session combined in parentheses
- "Nr. 2" style for qualifying rounds
- Sprint sessions introduced

---

### Format D: 2024 (Sponsor Names, Full Sessions)
**Pattern:** `Sponsor Grand Prix of Location Class Session`

**Examples:**
- `Qatar Airways Grand Prix of Qatar MotoGP Free Practice Nr. 1`
- `Qatar Airways Grand Prix of Qatar Moto3 Practice Nr. 1`
- `Qatar Airways Grand Prix of Qatar MotoGP Qualifying Nr. 1`
- `Qatar Airways Grand Prix of Qatar MotoGP Qualifying Nr. 2`
- `Qatar Airways Grand Prix of Qatar MotoGP Sprint Race`
- `Qatar Airways Grand Prix of Qatar Moto3 Race`
- `Qatar Airways Grand Prix of Qatar MotoGP Race`
- `Qatar Airways Grand Prix of Qatar MotoGP Warm Up`

**Notes:**
- Sponsor name prefix (Qatar Airways, etc.)
- No parentheses - flat format
- "Nr. 1", "Nr. 2" for numbered sessions
- "Sprint Race" (not just "Sprint")

**2024 Valencia Flood Special Case:**
| Original GP | Replacement | Circuit | Notes |
|-------------|-------------|---------|-------|
| Valencia GP (cancelled) | Motul Solidarity Grand Prix | Barcelona-Catalunya | Valencia cancelled due to October 2024 floods |

**TVDB Names:**
- `PETRONAS Grand Prix of Malaysia` - Round 19 (normal)
- `Motul Solidarity Grand Prix of Barcelona` - Round 20 (replacement finale)
- `Barcelona Official Test` - Season finale test

**File name implications for 2024:**
- Files named "valencia" for the finale → Will NOT match (cancelled GP)
- Files named "barcelona" for round 20 → Motul Solidarity Grand Prix
- Files named "solidarity" → Motul Solidarity Grand Prix
- Note: There IS a regular Barcelona (Catalunya) GP earlier in the season - don't confuse!

---

### Format E: 2025 (Clean Format, Abbreviated)
**Pattern:** `LOCATION - Circuit - Session` or `LOCATION - Circuit - Class race`

**Examples:**
- `THAILAND - Chang - FP 1`
- `THAILAND - Chang - Practice`
- `THAILAND - Chang - FP 2`
- `THAILAND - Chang - Q 1`
- `THAILAND - Chang - Q 2`
- `THAILAND - Chang - Sprint`
- `THAILAND - Chang - Warm Up`
- `THAILAND - Chang - Moto3 race`
- `THAILAND - Chang - Moto2 race`
- `THAILAND - Chang - MotoGP race`
- `ARG - Termas de Río Hondo - Q 1`
- `USA - COTA - Sprint`
- `QATAR - Lusail - FP 1`
- `AUSTRIA - Red Bull Ring - Q 2`
- `AUSTRALIA - Phillip Island - Sprint`
- `MALAYSIA - Sepang - Q 1`
- `MALAYSIA - Sepang - Q 2`
- `MALAYSIA - Sepang - Sprint`

**Sessions:** FP 1, FP 2, Practice, Q 1, Q 2, Sprint, Warm Up, [Class] race

**Notes:**
- Country/location in CAPS
- Circuit name after first dash
- Session after second dash
- Race episodes specify class (Moto3 race, Moto2 race, MotoGP race)
- Non-race sessions shared across classes (only one "Q 1" episode)

---

## Session Type Variations

### Qualifying
| File Names | TVDB Names |
|------------|------------|
| qualifying, quali, q | Qualifying |
| q1, quali1, qualifying1, qualifying.one | Q 1, Qualifying 1, Qualifying Nr. 1 |
| q2, quali2, qualifying2, qualifying.two | Q 2, Qualifying 2, Qualifying Nr. 2 |

### Practice
| File Names | TVDB Names |
|------------|------------|
| fp1, practice1, free.practice.1 | FP 1, Free Practice 1, Practice 1, Practice Nr. 1 |
| fp2, practice2 | FP 2, Free Practice 2, Practice Nr. 2 |
| fp3, practice3 | FP 3, Free Practice 3, Practice Nr. 3 |
| fp4, practice4 | FP 4, Free Practice 4, Practice Nr. 4 |
| practice | Practice, Free Practice |

### Race
| File Names | TVDB Names |
|------------|------------|
| race, main.race | Race, MotoGP race, MotoGP Race |
| sprint, sprint.race | Sprint, Sprint Race |

### Other
| File Names | TVDB Names |
|------------|------------|
| warmup, warm.up | Warm Up |
| test | Official Test |

---

## Location/Circuit Mapping

### Countries with Single Circuit
| File Names | TVDB Names | Circuit |
|------------|------------|---------|
| qatar, losail, lusail | Qatar, QATAR, Losail, Lusail | Losail/Lusail International Circuit |
| thailand, buriram, chang | Thailand, THAILAND, Chang, Buriram | Chang International Circuit |
| argentina, termas, arg | Argentina, ARG, Termas de Río Hondo | Termas de Río Hondo |
| malaysia, sepang | Malaysia, MALAYSIA, Sepang | Sepang International Circuit |
| australia, phillip.island | Australia, AUSTRALIA, Phillip Island | Phillip Island |
| austria, spielberg, red.bull.ring, osterreich, österreich | Austria, AUSTRIA, Red Bull Ring, Spielberg, Österreich | Red Bull Ring |
| netherlands, assen, dutch | Netherlands, Assen, Dutch | TT Circuit Assen |
| france, le.mans | France, Le Mans | Circuit of Le Mans |
| germany, sachsenring | Germany, Sachsenring | Sachsenring |
| czech, brno | Czech Republic, Brno | Automotodrom Brno |
| san.marino, misano | San Marino, Misano | Misano World Circuit |
| japan, motegi | Japan, Motegi | Twin Ring Motegi |
| indonesia, mandalika | Indonesia, Mandalika | Mandalika International Street Circuit |
| portugal, portimao, algarve | Portugal, Portimão, Algarve | Algarve International Circuit |
| india, buddh | India | Buddh International Circuit |
| great.britain, britain, silverstone, uk | Great Britain, Silverstone | Silverstone Circuit |

### Countries with Multiple Circuits (IMPORTANT)
| File Names | TVDB Names | Circuit | Notes |
|------------|------------|---------|-------|
| **USA** | | | |
| usa, america, americas, cota, austin | America, USA, COTA, Americas | Circuit of the Americas | Austin, Texas |
| miami | Miami | Miami International Autodrome | Florida |
| indianapolis, indy | Indianapolis | Indianapolis Motor Speedway | Indiana |
| las.vegas, vegas | Las Vegas | Las Vegas Strip Circuit | Nevada |
| **SPAIN** | | | |
| spain, jerez | Spain, Jerez | Circuito de Jerez | Andalusia |
| catalunya, catalonia, barcelona | Catalunya, Catalonia, Barcelona | Circuit de Barcelona-Catalunya | Catalonia |
| aragon | Aragon, Aragón | MotorLand Aragón | Aragon |
| valencia | Valencia | Circuit Ricardo Tormo | Valencia |
| **ITALY** | | | |
| italy, mugello | Italy, Mugello | Mugello Circuit | Tuscany |
| misano | Misano, San Marino | Misano World Circuit | Emilia-Romagna |
| monza | Monza | Autodromo Nazionale Monza | Lombardy |
| **JAPAN** | | | |
| japan, motegi | Japan, Motegi | Twin Ring Motegi | |
| suzuka | Suzuka | Suzuka Circuit | |

---

## Class Name Variations

| File Names | TVDB Names (various formats) |
|------------|------------------------------|
| motogp, moto.gp | MotoGP, Moto GP, MOTOGP, Moto GP |
| moto2, moto.2 | Moto2, Moto 2, MOTO2, Moto 2 |
| moto3, moto.3 | Moto3, Moto 3, MOTO3, Moto 3 |
| motoe, moto.e | MotoE, Moto E |

---

## Matching Strategy

### Simple Approach
1. **Parse file** → Extract: location keywords, session keywords, class (if any), year
2. **Normalize location** → Map to canonical location group
3. **Normalize session** → Map to session type
4. **Find episode** where title contains:
   - ANY location term from the group, AND
   - ANY session variation, AND
   - (if specified) class name

### Key Rules
1. **Austria vs Australia**: These are completely different - no substring matching!
   - Austria = Red Bull Ring, Spielberg
   - Australia = Phillip Island

2. **USA Circuits**: Must match specific circuit, not just "USA"
   - COTA/Austin ≠ Miami ≠ Indianapolis ≠ Las Vegas

3. **Spain Circuits**: Must match specific circuit
   - Jerez ≠ Catalunya ≠ Aragon ≠ Valencia

4. **2022 Special Case**: No session types - just match location + class

5. **Class Matching**: Handle spaces
   - "moto2" should match "Moto 2", "Moto2", "MOTO2"

---

## File Name Examples → Expected Matches

| File Name | Year | Expected Episode |
|-----------|------|------------------|
| `moto2.2022.austria.1080p` | 2022 | Grand Prix of Austria (Moto 2) - Red Bull Ring |
| `motogp.2025.malaysia.qualifying.two` | 2025 | MALAYSIA - Sepang - Q 2 |
| `motogp.2025.malaysia.qualifying.one` | 2025 | MALAYSIA - Sepang - Q 1 |
| `motogp.2025.malaysia.sprint.race` | 2025 | MALAYSIA - Sepang - Sprint |
| `MotoGP.2025x19.Australia.Qualifying` | 2025 | AUSTRALIA - Phillip Island - Q 2 (or Q 1?) |
| `f1.2024.austin.race` | 2024 | USA - COTA - Race |
| `motogp.2021.qatar.race` | 2021 | R1 - Qatar (Race) |
| `motogp.2023.portugal.sprint` | 2023 | Grand Prix of Portugal (MotoGP Sprint) |

---

---

# Formula 1 TVDB Naming Patterns

## F1 Format Summary by Year

| Years | Format | Example |
|-------|--------|---------|
| 2015-2018 | `Location (Session)` | `Australia (Race)` |
| 2019-2023 | `Location (Session)` + Testing episodes | `Bahrain (Qualifying)` |
| 2021+ | Added Sprint | `Great Britain (Sprint Qualifying)` |
| 2022+ | Sprint format | `Emilia Romagna (Sprint)` |
| 2023 | Sprint Shootout added | `Azerbaijan (Sprint Shootout)` |
| 2024 | `Round #: Location (Session)` | `Round 1: Bahrain (Race)` |
| 2025 | `Formula 1 Sponsor GP Year - Session` | `Formula 1 Louis Vuitton Australian Grand Prix 2025 - Race` |

---

## F1 Format A: 2015-2023 (Simple Location-Based)
**Pattern:** `Location (Session)`

**Examples:**
- `Australia (Practice 1)`
- `Australia (Practice 2)`
- `Australia (Practice 3)`
- `Australia (Qualifying)`
- `Australia (Race)`
- `Bahrain (Race)`
- `Monaco (Qualifying)`
- `Great Britain (Sprint Qualifying)` (2021)
- `Emilia Romagna (Sprint)` (2022+)
- `Azerbaijan (Sprint Shootout)` (2023+)

**Sessions:** Practice 1-3, Qualifying, Race, Sprint, Sprint Qualifying (2021), Sprint Shootout (2023+)

**Testing Episodes:** `Testing - Day 1, Session 1` or `Test 1 - Day 1, Session 1`

**Notes:**
- Very consistent format for 9 years
- Sprint races added in 2021 (initially "Sprint Qualifying")
- Sprint Shootout added in 2023

**2020 COVID Special Cases (same circuit, different GP names):**
| TVDB Name | Actual Circuit | Notes |
|-----------|----------------|-------|
| Austria | Red Bull Ring | First Austrian GP |
| Steiermark | Red Bull Ring | Second Austrian GP (week after Austria) |
| Great Britain | Silverstone | First British GP |
| 70th Anniversary | Silverstone | Second British GP (celebrating F1's 70th anniversary) |
| Italy | Monza | Italian GP |
| Toscana | Mugello | Tuscan GP (Ferrari's 1000th race) |
| Eifel | Nürburgring | Eifel GP (Germany, first time since 2013) |
| Bahrain | Bahrain International Circuit | First Bahrain GP (full layout) |
| Sakhir | Bahrain International Circuit | Second Bahrain GP (outer layout) |
| Emilia Romagna | Imola | First Imola race since 2006 |

**File name implications for 2020:**
- Files named "styria" or "steiermark" → Steiermark GP
- Files named "austria" → Austria GP (first one)
- Files named "70th" or "anniversary" → 70th Anniversary GP
- Files named "silverstone" or "britain" → Could be either! Need date context
- Files named "toscana" or "tuscan" or "mugello" → Toscana GP
- Files named "eifel" or "nurburgring" → Eifel GP
- Files named "sakhir" → Sakhir GP (second Bahrain)
- Files named "bahrain" → Bahrain GP (first one)
- Files named "imola" or "emilia" → Emilia Romagna GP

**2021 Special Cases:**
| TVDB Name | Actual Circuit | Notes |
|-----------|----------------|-------|
| Austria | Red Bull Ring | First Austrian GP |
| Styria | Red Bull Ring | Second Austrian GP (week before Austria!) |
| Great Britain (Sprint Qualifying) | Silverstone | First ever F1 Sprint introduced |
| Italy (Sprint Qualifying) | Monza | Second Sprint event |
| São Paulo (Sprint Qualifying) | Interlagos | Third Sprint event |

**Sprint Qualifying (2021 only):**
- In 2021, the Sprint race format was called "Sprint Qualifying"
- Episodes: Practice 1, Practice 2, Qualifying, Sprint Qualifying, Race
- Only 3 events had Sprint Qualifying: Great Britain, Italy, São Paulo

**File name implications for 2021:**
- "styria" or "steiermark" → Styria GP
- "austria" → Austria GP
- Files with "sprint" at GB/Italy/Brazil → Sprint Qualifying episode

---

## F1 Format B: 2024 (Round Numbers Added)
**Pattern:** `Round #: Location (Session)`

**Examples:**
- `Round 1: Bahrain (Practice 1)`
- `Round 1: Bahrain (Race)`
- `Round 3: Australia (Qualifying)`
- `Round 5: China (Sprint Shootout)`
- `Round 5: China (Sprint)`
- `Round 6: Miami (Practice)`

**Sessions:** Practice 1-3 or just "Practice", Qualifying, Race, Sprint Shootout, Sprint

**Notes:**
- Round numbers added as prefix
- Sprint weekends have: Practice, Sprint Shootout, Sprint, Qualifying, Race
- Regular weekends have: Practice 1-3, Qualifying, Race

---

## F1 Format C: 2025 (Full Sponsor Names)
**Pattern:** `Formula 1 Sponsor Location Grand Prix Year - Session`

**Examples:**
- `Formula 1 Louis Vuitton Australian Grand Prix 2025 - Practice 1`
- `Formula 1 Louis Vuitton Australian Grand Prix 2025 - Qualifying`
- `Formula 1 Louis Vuitton Australian Grand Prix 2025 - Race`
- `Formula 1 Heineken Chinese Grand Prix 2025 - Sprint Qualifying`
- `Formula 1 Heineken Chinese Grand Prix 2025 - Sprint Race`
- `Formula 1 Lenovo Japanese Grand Prix 2025 - Race`
- `Formula 1 Gulf Air Bahrain Grand Prix 2025 - Race`
- `Formula 1 STC Saudi Arabian Grand Prix 2025 - Race`
- `Formula 1 Crypto.com Miami Grand Prix 2025 - Sprint Race`
- `Formula 1 AWS Gran Premio del Made in Italy e dell'Emilia-Romagna 2025 - Race`

**Sessions:** Practice 1-3 or "Practice", Qualifying, Race, Sprint Qualifying, Sprint Race

**Testing:** `Formula 1 Aramco Pre-Season Testing 2025 (Day 1) - Session 1`

**Notes:**
- Full sponsor names included (Louis Vuitton, Heineken, Lenovo, etc.)
- Year included in title
- Session after dash (not in parentheses)
- "Sprint Race" instead of just "Sprint"
- "Sprint Qualifying" instead of "Sprint Shootout"
- Some non-English names: "Gran Premio del Made in Italy e dell'Emilia-Romagna"

---

## F1 Session Type Variations

### Qualifying
| File Names | TVDB Names |
|------------|------------|
| qualifying, quali, q | Qualifying |

### Practice
| File Names | TVDB Names |
|------------|------------|
| fp1, practice1, free.practice.1 | Practice 1, FP1 |
| fp2, practice2 | Practice 2, FP2 |
| fp3, practice3 | Practice 3, FP3 |
| practice | Practice |

### Race
| File Names | TVDB Names |
|------------|------------|
| race | Race |
| sprint | Sprint, Sprint Race, Sprint Qualifying (2021 F1 only) |
| sprint.shootout | Sprint Shootout (2023+ F1) |
| sprint.qualifying | Sprint Qualifying (F1 2021), also Sprint Shootout (2023+) |

---

## F1 Location Mapping

### Standard Locations
| File Names | TVDB Names |
|------------|------------|
| australia, melbourne | Australia, Australian |
| bahrain, sakhir | Bahrain |
| saudi, saudi.arabia, jeddah | Saudi Arabia, Saudi Arabian |
| china, shanghai | China, Chinese |
| japan, suzuka | Japan, Japanese |
| miami | Miami |
| emilia, imola | Emilia Romagna, Emilia-Romagna |
| monaco | Monaco |
| spain, barcelona, catalunya | Spain, Spanish |
| canada, montreal | Canada, Canadian |
| austria, spielberg, red.bull.ring | Austria, Austrian, Steiermark |
| britain, silverstone, uk | Great Britain, British |
| hungary, budapest, hungaroring | Hungary, Hungarian |
| belgium, spa | Belgium, Belgian |
| netherlands, zandvoort | Netherlands, Dutch |
| italy, monza | Italy, Italian |
| azerbaijan, baku | Azerbaijan |
| singapore, marina.bay | Singapore |
| usa, austin, cota, americas | USA, United States, Americas |
| mexico, mexico.city | Mexico, Mexican |
| brazil, interlagos, sao.paulo | Brazil, Brazilian, São Paulo |
| las.vegas, vegas | Las Vegas |
| qatar, lusail | Qatar |
| abu.dhabi, yas.marina | Abu Dhabi |

---

## F1 File Name Examples → Expected Matches

| File Name | Year | Expected Episode |
|-----------|------|------------------|
| `f1.2015.australia.race` | 2015 | Australia (Race) |
| `f1.2020.austria.qualifying` | 2020 | Austria (Qualifying) |
| `f1.2020.steiermark.race` | 2020 | Steiermark (Race) |
| `f1.2022.emilia.romagna.sprint` | 2022 | Emilia Romagna (Sprint) |
| `f1.2023.azerbaijan.sprint.shootout` | 2023 | Azerbaijan (Sprint Shootout) |
| `f1.2024.bahrain.race` | 2024 | Round 1: Bahrain (Race) |
| `f1.2024.china.sprint` | 2024 | Round 5: China (Sprint) |
| `f1.2025.australia.race` | 2025 | Formula 1 Louis Vuitton Australian Grand Prix 2025 - Race |
| `f1.2025.miami.sprint.race` | 2025 | Formula 1 Crypto.com Miami Grand Prix 2025 - Sprint Race |

---

---

# Combined Matching Strategy

## Matching Algorithm

### Step 1: Parse File Name
Extract from filename/folder:
- **Location keywords** (e.g., "australia", "misano", "silverstone")
- **Session keywords** (e.g., "race", "qualifying", "q1", "sprint")
- **Class** (e.g., "motogp", "moto2", "moto3") - MotoGP only
- **Year/Season** (e.g., 2024, 2025)

### Step 2: Find Matching Episodes
Search all episodes in the season where title contains:
- ANY location variation from the parsed location, AND
- ANY session variation from the parsed session, AND
- (if MotoGP race) matching class name

### Step 3: Handle Results

**Single Match Found** → Use that episode ✓

**Multiple Matches Found (Double-Header Scenario)**:
1. Check if filename contains a SPECIFIC GP name that disambiguates:
   - "styria" vs "austria" → use Styria GP
   - "doha" vs "qatar" → use Doha GP
   - "emilia" or "romagna" vs "san marino" or "misano" → use Emilia Romagna GP
   - "andalucia" vs "spain" or "jerez" → use Andalucia GP
   - "70th" or "anniversary" vs "britain" → use 70th Anniversary GP
   - "sakhir" vs "bahrain" → use Sakhir GP
   - "solidarity" vs "barcelona" (2024) → use Solidarity GP

2. If still ambiguous, use **file date hinting**:
   - Get file's `LastWriteTime` via `_diskProvider.FileGetLastWrite(path)`
   - Compare to each matching episode's `AirDateUtc`
   - If file date is within **7 days** of one episode's air date → suggest that match
   - If file date is closer to one episode by **more than 14 days** difference → confident match

3. If still ambiguous → **No match** (flag for manual intervention)

**No Match Found** → Return empty (standard Sonarr behavior)

---

## Known Double-Header Circuits (Ambiguous Cases)

These circuit names require disambiguation:

| Circuit | Years | Possible GPs |
|---------|-------|--------------|
| Jerez | 2020 | Spain, Andalucia |
| Red Bull Ring | 2020, 2021 | Austria, Styria |
| Misano | 2020, 2021 | San Marino, Emilia Romagna |
| MotorLand Aragón | 2020 | Aragón, Teruel |
| Valencia | 2020 | Europe, Valencia |
| Losail | 2021 | Qatar, Doha |
| Portimão | 2021 | Algarve, Portugal |
| Silverstone | 2020 | Great Britain, 70th Anniversary |
| Mugello | 2020 | Italy (none), Toscana |
| Bahrain | 2020 | Bahrain, Sakhir |
| Barcelona | 2024 | Catalunya GP, Solidarity GP |

---

## Technical Implementation Notes

### Available Data During Import
- **File path**: `localEpisode.Path`
- **File last write time**: `_diskProvider.FileGetLastWrite(localEpisode.Path)` returns `DateTime` (UTC)
- **Episode air date**: `episode.AirDateUtc` is `DateTime?`
- **Episode title**: `episode.Title` is string

### Injection Required
`FindRacingEpisode` currently doesn't have access to `IDiskProvider`. Options:
1. Pass file path + disk provider to the method
2. Add file date as a parameter
3. Create a new method for ambiguous cases

### Threshold Constants
```csharp
private const int ConfidentMatchDaysDifference = 14; // Days difference to confidently choose one
private const int SuggestMatchDaysWindow = 7;        // Days from air date to suggest match
```

---

## Next Steps

1. Implement disambiguation logic for specific GP names (styria, doha, etc.)
2. Add file date comparison as fallback
3. Return no match for truly ambiguous cases
4. Handle year-specific TVDB formats in title matching
