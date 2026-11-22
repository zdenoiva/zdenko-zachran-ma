# Zdenko, zachráň ma! — changelog

## [2.28] - 2026-11-22

- **Oprava priameho ATA čítania pre USB mostíky**:
  - poradie variantov je teraz ako v CrystalDiskInfo: najprv štandardné SAT-12
    (`A1 08 0E`), potom SAT-16, ASM1352R, JMicron, Sunplus, Cypress a Prolific
  - opravené flagy 12-bajtového CDB (`0E` namiesto `2E`)
  - opravená štruktúra `SCSI_PASS_THROUGH` (`DataBufferOffset` je `ULONG_PTR`),
    takže funguje aj záložná ne-direct cesta (predtým vracala chybu 1306)
  - SMART sa skúsi aj vtedy, keď Windows o disku nehlási nič
    (predtým sa v takom prípade priame ATA čítanie vôbec nespustilo)
  - skúšanie variantov má časový rozpočet, aby sa UI nezablokovalo
- **Disky, ktoré `Get-Disk` vynechá** (prázdny, RAW, offline), sa dopĺňajú
  z `Win32_DiskDrive` — zobrazia sa v Sprievodcovi, v zozname diskov aj
  v diagnostike a načíta sa im SMART.
- **Bezpečnosť pri diagnostike**:
  - diagnostika posiela len `INQUIRY`, `VPD 0x80` a `ATA PASS-THROUGH`;
    rizikové `REPORT LUNS`, `VPD 0x83`, `VPD 0x00` a `READ CAPACITY` sa už neposielajú
  - pred spustením je varovanie, že na chybnom USB mostíku môže systém spadnúť
  - diagnostika beží mimo UI vlákna
  - globálny zámok `Local\ZdenkoZachranMa-scsi` bráni tomu, aby dve inštancie
    programu posielali SCSI príkazy do toho istého mostíka súčasne
  - núdzový vypínač `ZDENKO_NO_SCSI=1` vypne všetky priame SCSI/ATA príkazy
- Diagnostika navyše vypisuje reťazec PNP zariadení (VID/PID mostíka),
  SCSI adresu, INQUIRY, VPD 0x80, výsledok každého CDB variantu a SCSI chybu.

## [2.27] - 2026-11-22

- **Ďalšie USB mostíky pre SMART a identitu**:
  - Cypress (vendor CDB `24 24 00 BE …`)
  - Prolific (vendor CDB `D8 15 …`)
  - doplnené k ASM1352R, JMicron, Sunplus a štandardnému SAT
  - pridaný fallback cez klasické `SCSI_PASS_THROUGH` (nie direct), ktoré
    niektoré USB ovládače vyžadujú
- **Diagnostika USB/SMART priamo v programe**:
  - **O programe → Uložiť USB/SMART diagnostiku…**
  - report obsahuje Windows PnP, USB VID/PID, SCSI adresu, INQUIRY, ATA IDENTIFY,
    použitý protokol, SMART atribúty a presnú SCSI chybu
  - rovnaký report sa dá vytvoriť cez `ZachranaDisku.exe --usbdiag`
    (súbor `%TEMP%\ZdenkoZachranMa-usbdiag.txt`)
- Zámok disku z 2.26 zostáva: kontrola povrchu, oprava povrchu a vymazanie
  sú chránené proti druhej inštancii programu.

## [2.26] - 2026-11-22

- **Univerzálnejšie S.M.A.R.T. a identifikácia pre USB doku/klonovací box**:
  - ASM1352R: ATA PASS-THROUGH(12) s protokolom `0x0D` pre SATA port 0
    a `0x0E` pre SATA port 1 (výber podľa LUN)
  - JMicron: vendor CDB `DF 10 …` pre SMART aj IDENTIFY
  - Sunplus: vendor CDB `F8 00 22 10 …`
  - štandardné SATA PASS-THROUGH ostáva ako záloha
  - identita sa skúša cez ATA IDENTIFY a potom cez SCSI INQUIRY (VPD 0x80)
  - do logu sa zapisuje, ktorý protokol uspel (`85/08`, `A1/1A`, …)
- **Zámok disku počas kontroly povrchu, opravy povrchu a vymazania**:
  - medziprocesový zámok `%LOCALAPPDATA%\ZdenkoZachranMa\locks\disk-N.lock`
  - druhá inštancia programu dostane jasnú hlášku, že disk sa práve používa
  - zámok sa uvoľní po dokončení, zrušení alebo chybe; po páde procesu
    ho uvoľní Windows
- Overené: AXAGON ADSA-DC (dva disky, SMART + reálne SN), zámok medzi
  dvoma spustenými procesmi (druhý `acquired=False`).

## [2.25] - 2026-11-21

- **Každý SMART problém je teraz vysvetlený vľavo v Sprievodcovi** — čo znamená,
  aké sú riziká a čo robiť:
  - CRC chyby prenosu (kábel/port/mostík)
  - presunuté, nestabilné a neopraviteľné sektory
  - chyby média, neopravené chyby čítania/zápisu
  - vysoká teplota, vek disku, opotrebenie SSD, náhradné bloky
  - pomalý rýchly test čítania
- **CRC chyby sa už nezamlčia**: pribudnú do upozornenia a karty
  („chyby CRC: 1 – skontrolujte kábel/port“).
- Pri nezávažných nálezoch je lišta **žltá (UPOZORNENIE)**, nie červená;
  zdravie sa zníži maximálne na **94 % (Dobrý)**, aby nesvietilo „Výborný“.
- Overené na WDC WD5000AAKX v doku: 94 %, Dobrý, CRC 1 s vysvetlením
  a odporúčaním skontrolovať kábel/port.
- Dlhší popis sa zalomí podľa šírky okna; hlavička skráti dlhý model „…“,
  takže sa nerozšíri mimo okna.

## [2.24] - 2026-11-21

- **S.M.A.R.T. a sériové čísla pre dvojdiskový USB dok AXAGON ADSA-DC**:
  - dok vracia SCSI stav „CHECK CONDITION“ s fixed-format sense `70 00 01`
    (RECOVERED ERROR) — ten sa teraz správne vyhodnotí ako úspech
  - ATA príkaz sa adresuje na správny LUN (`PathId/TargetId/Lun`), takže
    druhý disk v doku sa už nečíta ako prvý
  - skutočný **model a sériové číslo** sa čítajú cez **ATA IDENTIFY DEVICE**
    (Windows pri doku hlásil pre oba disky „Generic Physical Disk“ a rovnaké SN)
  - identifikácia USB diskov prebieha paralelne s limitom, aby sa štart nezdržal
  - ATA čítanie má časový limit, takže zaspávajúci disk nezablokuje diagnostiku
- Overené na AXAGON ADSA-DC s dvoma diskami:
  - **TOSHIBA DT01ACA100**, S/N `27LDW30NS`, 17 SMART atribútov, 100 %, 33 °C, 7 039 h
  - **WDC WD5000AAKX-00ERMA0**, S/N `WD-WCC2EMS28839`, 17 SMART atribútov, 97 %, 34 °C, 2 056 h
- Sprievodca bezpečne aktualizuje kartu aj vtedy, keď sa výsledok vráti z iného
  vlákna (žiadne zaseknutie na „Zisťujem stav disku…“).

## [2.23] - 2026-11-15

- **S.M.A.R.T. v dvojdiskových USB doku/klonovacom systéme**:
  - pred ATA príkazom sa zisťuje skutočná SCSI adresa (`PathId / TargetId / Lun`)
    cez `IOCTL_SCSI_GET_ADDRESS`
  - tá sa nastaví do ATA PASS-THROUGH, takže druhý disk v doku sa už neadresuje
    ako prvý (predtým oba používali Lun 0 — rovnaké SN/model aj SMART)
  - do logu sa zapisuje adresa a dôvod zlyhania ATA čítania
- Ak mostík ATA príkazy vôbec nepodporuje, log teraz obsahuje presnú SCSI chybu,
  podľa ktorej sa dá doplniť špecifický protokol pre daný mostík.

## [2.22] - 2026-11-15

- **Inštalátor/aktualizácia už nekoliduje s bežiacou aplikáciou**:
  - pred kopírovaním súborov skontroluje, či `ZachranaDisku.exe` beží
  - ak áno, ponúkne: „Aplikácia práve beží. Ukončiť ju a pokračovať?“
  - po potvrdení ju najprv zavrie slušne (WM_CLOSE), potom v prípade potreby
    vynútene ukončí a čaká na ukončenie
  - ak ju zavrieť nemožno alebo používateľ odmietne, inštalácia sa **zruší**
  - pred samotným kopírovaním ešte raz overí, že aplikácia nebeží
  - tichá aktualizácia (`/AUTOUPDATE`) ukončí bežiacu aplikáciu automaticky
    (bez dialógu), takže in-app aktualizácia prebehne spoľahlivo

## [2.21] - 2026-11-15

- **S.M.A.R.T. pre disky za USB-SATA mostíkmi a v klonovacích/dokovacích systémoch**:
  - nové priame ATA čítanie cez SCSI ATA PASS-THROUGH (16 aj 12), ktoré obchádza
    WMI a funguje tam, kde Windows SMART neposkytne
  - načítajú sa atribúty ako v HD Tune: presunuté/nestabilné/neopraviteľné sektory,
    teplota, hodiny, cykly napájania, neočakávané vypnutia, životnosť SSD
  - každý disk sa čítá cez svoj `\\.\PhysicalDriveN`, takže v doku sa spracujú oba
- Oprava falošne zlého zdravia: životnosť sa už neodvodzuje z normalizovaných
  atribútov 173/177 (mohli ukázať napr. 32 % namiesto 100 %).
- Sériové číslo samé nuly (bežné pri USB mostíkoch) sa už nezobrazuje ako
  `0000000000000000`; použije sa disk číslo namiesto falošného S/N.
- Overené na WDC WDS250G2B0A v USB doku: 25 atribútov, životnosť 100 %,
  1 688 cyklov, 39 neočakávaných vypnutí, 0 vadných sektorov.

## [2.20] - 2026-11-15

- **Oprava dlhého čakania „Zisťujem stav partície...“ pri disku bez partície**:
  - pri celom disku bez súborového systému sa už nespúšťa rýchly test čítania
    (zbytočne zdržoval a pri prázdnom/USB disku mohol visieť)
  - S.M.A.R.T. má pri takomto disku limit **3 sekundy** namiesto 30; ak Windows
    neodpovie, karta ukáže „nedostupné“ a Sprievodca pokračuje
  - prázdny disk sa vyhodnotí za približne **2 sekundy**
- Do logu sa zapisuje čas S.M.A.R.T. kontroly a rýchleho testu čítania,
  aby sa prípadné spomalenie dalo ľahšie nájsť.

## [2.19] - 2026-11-15

- **Hlavný zoznam v Sprievodcovi zobrazuje všetky disky**, aj keď nemajú
  použiteľnú partíciu — nový, prázdny, RAW alebo offline disk.
  - taký disk sa zobrazí ako „Disk N · celý disk“ a dá sa vybrať
  - nepoužiteľné kroky sú **šedé** (napr. offline disk)
- **Sprievodca pre prázdny/RAW disk** už neponúka kopírovanie súborov ani opravu:
  - **Plná kontrola povrchu**
  - **Vytvoriť partíciu** — otvorí záložku Disk s vybraným diskom
    a predvyplnenou voľbou vytvoriť jednu partíciu
  - **Záchrana súborov** (hĺbková) pre prípad, že tam predtým boli dáta
- RAW partícia s tabuľkou partícií ponúka aj kontrolu povrchu.
- Offline disk upozorní „Disk je offline“ a všetky akcie sú nedostupné.
- Disk len na čítanie ponúkne kontrolu povrchu a záchranu, ale vytvorenie
  partície je zablokované.
- Rýchly test čítania sa preskakuje pre virtuálne disky (VHD), aby sa
  nezobrazovali nerealistické rýchlosti z pamäte.
- Tlačidlá v Sprievodcovi už nemajú pevné čísla 1/2/3 — menia sa podľa situácie.

## [2.18] - 2026-11-09

- **Oprava falošného „Windows 10/11: CHÝBA“** na niektorých PC s Windows 11:
  - build sa už nečíta cez `Environment.OSVersion`, ktorý pri zapnutom
    kompatibilnom režime hlási nepravdivé číslo (napr. 6002 = Vista SP2)
  - nový build sa čítá z registry `CurrentBuildNumber` a ako záloha cez
    `RtlGetVersion` (obchádza kompatibilné režimy)
  - kontrola zobrazí napr. „Windows 11 · build 22631 · 64-bit“
- Ak je Windows skutočne starší ako 10, kontrola ho stále správne odmietne.

## [2.17] - 2026-11-09

- **Rýchly test čítania pri diagnostike** (Sprievodca):
  - 12 vzoriek po 4 MB (48 MB) — hustejšie na začiatku disku, zvyšok rovnomerne
  - maximálne 6 sekúnd, takže štart sa nezdrží
  - meria priame čítanie bez cache, ak to radič podporuje
  - rýchlosť sa porovnáva podľa typu disku a zbernice (USB, SATA, NVMe, SSD/HDD)
- **Zdravie disku už neukazuje 100 %, keď je disk podozrivý**:
  - pomalé čítanie zníži zdravie a pridá „odporúčaná kontrola povrchu“
  - viac ako 15 000 hodín (625 dní) zníži zdravie a pridá „zvážte výmenu“
  - viac ako 30 000 hodín odporúča výmenu disku
  - chyba čítania alebo časový limit testu zníži zdravie výraznejšie
- Karta stavu zobrazuje **Čítanie** (MB/s) farebne podľa výsledku.
- Pri pomalom disku pribudne tlačidlo **Plná kontrola povrchu**, ktoré otvorí
  záložku Povrch s vybraným diskom.
- Overené na WDC WDS250G2B0A (USB SSD, 15 383 h, 640 dní):
  zdravie **80 % — Pozor**, čítanie **39,5 MB/s**, upozornenie na vek aj pomalé čítanie.
- „O programe“ doplnené o rýchly test čítania a hodnotenie veku disku.

## [2.16] - 2026-11-09

- **Lišta aktualizácie**: percento sťahovania (napr. `18 %`, `100 %`) sa už
  nezalamuje na dva riadky; stĺpec je širší a číslo je vždy na jednom riadku.
- **Kruhový ukazovateľ zdravia**: percento je presne vycentrované v kruhu.
- **O programe** obsahuje kompletný zoznam funkcií vrátane aktualizácií,
  rozsahu kontroly povrchu, mapy, štatistík a SMART/NVMe karty.
  Sekcia s nepoužitými nástrojmi je odstránená.
- **Stabilné rozloženie pri zmene veľkosti okna**:
  - hlavička má pevné dvojriadkové rozloženie (disk, tlačidlá, info, administrátor)
  - záložky Boot, Disk, Diagnóza, Oprava, Protokol a Záchrana súborov už
    nezalamujú tlačidlá na ďalší riadok
  - Oprava má dva pevné riadky tlačidiel, Záchrana súborov tri
  - dlhé varovné texty sa zalamujú podľa šírky okna namiesto odrezania
- Povrch, Záchrana súborov a nové karty si držia rovnaké pozície tlačidiel
  pri zväčšení aj zmenšení okna.

## [2.15] - 2026-11-09

- **Grafická karta stavu disku v Sprievodcovi** (vpravo od verdiktu, podobne ako
  CrystalDiskInfo):
  - kruhový ukazovateľ zdravia 0–100 % s hodnotením Výborný / Dobrý / Pozor / Kritický
  - teplota, zostávajúca životnosť SSD, hodiny a dni v prevádzke
  - presunuté, nestabilné a neopraviteľné sektory, chyby média a CRC
  - pri NVMe zápis/čítanie v TB a náhradné bloky
- **NVMe SMART/Health log page** cez Windows `DeviceIoControl`:
  - opotrebenie v % (rovnaké ako CrystalDiskInfo), náhradné bloky a ich prah,
    critical warning, media errors, unsafe shutdowns, dátové jednotky
  - overené na NVMe HFS001TEJ9X125N: 97 % zdravie, 3 % opotrebenie, 58 °C,
    9 039 h, zápis 49,4 TB, čítanie 96,8 TB
- **ATA/SATA SMART**: načítanie všetkých atribútov a presnejšie hodnotenie
  (5, 9, 12, 174, 187, 190/194, 197, 198, 199, 231/233, 241/242)
- Oprava priradenia SMART atribútov: atribúty sa už nepriradia inému disku,
  keď sa model nezhoduje.
- Karta zdravia zobrazuje aj dôvody alertu; pri čistom disku sa zbytočný
  textový S.M.A.R.T. riadok skryje.
- Health score sa počíta z opotrebenia, náhradných blokov, sektorov, médií,
  teploty a varovaní; pri vážnych chybách odporúča výmenu disku.

## [2.14] - 2026-11-09

- **Mapa povrchu ako HD Tune**:
  - jeden štvorec = väčší blok disku (pri 465 GB ≈ 236 MB), takže mapa má
    približne 2 000 prehľadných blokov namiesto 119 000 miniatúrnych bodov
  - žiadne čierne rámiky medzi blokmi, mapa vypĺňa celú plochu
  - svetlé pozadie podľa našej témy namiesto šedej mriežky
  - rovnaké farby v mape aj v legende
- **Rozsah kontroly Od bloku / Do bloku**:
  - kontrola sa dá spustiť od ľubovoľného bloku, napr. pokračovať po nedobehnutej kontrole
  - nová kontrola od bloku 0 mapu vymaže, pokračovanie od iného bloku ju doplní
  - po zrušení kontroly zostanú namerané bloky v mape
- **Výraznejšie štatistiky**: poškodené bloky v % a počte, rýchlosť, pozícia,
  uplynulý čas, ETA a veľkosť jedného štvorca mapy
- **Tlačidlo Opraviť** má pevné miesto vpravo a už neodskakuje naľavo
- Oprava vypnutého tlačidla má sivý vzhľad namiesto červenej.

## [2.13] - 2026-11-09

- **Aktualizácia nainštalovanej verzie**: inštalátor sa už nespúšťa počas behu programu.
  Program sa najprv zavrie, pomocný skript počká na jeho ukončenie a až potom spustí
  tichú inštaláciu. Odstraňuje chybu „Nepodarilo sa nainštalovať …ZachranaDisku.exe“.
- **Záchrana súborov**: prechod z načítavania súborov na opravu už nehlási falošné
  „práve prebieha operácia“. Načítavanie sa pri prechode na opravu bezpečne zruší;
  hláška zostáva len pri skutočnom kopírovaní.
- Záložka Záchrana súborov sa už nevyberá cez pevný index, ale cez svoju skutočnú
  záložku (po pridaní záložiek Disk a Povrch sa indexy posunuli).
- Diagnostika **dirty** NTFS už netvrdí, že Windows disk blokuje. Nové znenie:
  „NTFS je čitateľné, ale volume je označený ako dirty“ s vysvetlením, že obsah je
  čitateľný a najprv treba skopírovať súbory a potom spustiť chkdsk.
- Overené na disku D: (WDC WD5000LPVX, 465,75 GB NTFS, dirty bit).

## [2.12] - 2026-11-09

- Zarovnané pole **Systém súborov** a **Písmeno** v záložke Disk.
- Lišta aktualizácie má pevnú výšku riadku; po načítaní disku sa už neskryje.
- Nová záložka **Povrch**:
  - čítanie celého disku po 4 MB blokoch
  - vizuálna mapa OK / pomalé / chybné bloky
  - počet vadných sektorov, rýchlosť, ETA a možnosť zrušiť
  - CSV report
- Oprava vadných sektorov:
  - opakované čítanie a záloha čitateľných dát
  - zápis späť alebo nuly na vynútenie preradenia
  - overenie po zápise
- Testované na 512 MB a 2 GB VHDX; scan, mapa, report a oprava sektora prešli.

## [2.11] - 2026-11-09

- Nová záložka **Disk** na vymazanie celého disku.
- Rýchle vymazanie odstráni partície; dáta môžu byť ešte obnoviteľné.
- Úplné prepísanie nulami prepíše celý disk a overí začiatok/koniec.
- Po vyčistení vie vytvoriť jednu partíciu NTFS/FAT32, GPT/MBR, s písmenom
  a názvom.
- Aktuálny systémový disk a disk, z ktorého program beží, sú zablokované.
- Operácia vyžaduje potvrdenie textom a zaškrtnutie súhlasu.
- Pri úplnom prepise je viditeľný priebeh a možnosť zrušiť.
- Testované: rýchle vymazanie, FAT32 aj NTFS, GPT aj MBR, úplné nulovanie
  a opätovné vytvorenie partície.

## [2.10] - 2026-11-09

- Opravené zmiznutie tlačidla **Aktualizovať** a **X** po načítaní disku.
- Hlavička s dlhým popisom disku už nerozširuje celé okno do šírky.
- Lišta aktualizácie zostáva po načítaní disku plne viditeľná a klikateľná.
- Dopočítaná minimálna výška lišty, aby bolo X celé.

## [2.9] - 2026-11-09

- Opravené spustenie programu po dokončení inštalácie.
- Inštalátor teraz spúšťa `ZachranaDisku.exe` cez `ShellExecute`, takže
  aplikácia sa správne spustí s administrátorskými právami.
- Opravená chyba `CreateProcess zlyhala; kód 740` na poslednej obrazovke
  inštalátora.

## [2.8] - 2026-11-09

- Oznam o novej verzii má vyšší riadok a plne viditeľné tlačidlo X.
- Tlačidlo **Aktualizovať** priamo stiahne novú verziu s overením SHA256.
- **Inštalovaná verzia:** stiahne sa inštalátor, program sa zavrie, tiho sa nainštaluje a spustí sa nová verzia.
- **Prenosná verzia:** stiahne sa nový EXE, po zavretí programu sa prepíše pôvodný súbor a spustí sa nová verzia.
- Pri sťahovaní je v lište viditeľný priebeh v percentách.
- Inštalátor podporuje tichú auto-aktualizáciu cez `/AUTOUPDATE`.

## [2.7] - 2026-11-09

- Opravené rozpoznávanie MBR boot code: disk signature sa už nepovažuje za boot kód.
- Opravené vytvorenie `{ramdiskoptions}` pri pridávaní Windows RE / OEM recovery.
- Testované na klonovanom disku:
  - odstránený `bootmgr` a BCD → BIOS oprava ich obnovila
  - vynulovaný MBR boot code → `bootsect` ho obnovil
  - odstránené UEFI `bootmgfw.efi` a BCD → UEFI oprava ich obnovila
  - MBR → GPT konverzia cez `mbr2gpt` prešla
  - OEM recovery záznam sa pridal do BCD
- Oprava aktuálneho systémového disku je naďalej zablokovaná.

## [2.6] - 2026-11-09

- Nová záložka **Boot** na kontrolu a opravu bootovania Windows.
- Detekcia UEFI/BIOS, GPT/MBR, ESP, active partície, BCD a recovery.
- Oprava UEFI cez `bcdboot /f UEFI` a BIOS cez `bcdboot /f BIOS` + `bootsect`.
- Záloha BCD, boot súborov, MBR a VBR pred každou opravou.
- Oprava je povolená len pre iné disky; aktuálny systémový disk je blokovaný.
- Voliteľná konverzia MBR → GPT cez `mbr2gpt` s varovaním na BIOS a radiče.
- Pridanie Windows RE a OEM recovery (WIM) do boot menu.
- Popis nastavení BIOS/UEFI a upozornenie na SATA/RAID/AHCI radiče.

## [2.5] - 2026-11-09

- Prenosný súbor premenovaný na `ZachranaDisku.exe`; názov aplikácie zostáva „Zdenko, zachráň ma!“.
- Inštalátor má názov `ZachranaDisku-Setup-2.5.exe`.
- Pridaná kontrola novšej verzie pri spustení aplikácie.
- Nová stránka na stiahnutie na GitHub Pages.
- Prenosná verzia a inštalátor sa vydávajú cez GitHub Releases.
- `latest.json` obsahuje verziu, odkazy, veľkosť a SHA256.
- Doplnené vydávanie cez `release.ps1`.

## [2.4] - 2026-11-06

- Kontrola prostredia pri prvom spustení je tichá; ak je všetko v poriadku, okno sa nezobrazí.
- Chýbajúce súčasti sa automaticky doinštalujú a info okno sa samo zavrie.
- V Diagnóze pribudla kontrola systémových súborov Windows cez DISM CheckHealth.
- Pri zistení poškodenia ponúkne opravu cez DISM RestoreHealth + SFC /scannow.

## [2.3] - 2026-11-06

- Inštalátor ponúka inštaláciu alebo spustenie bez inštalácie (prenosná verzia).
- Prenosná verzia zostáva jeden self-contained súbor.

## [2.2] - 2026-11-05

- Nová moderná ikona disku s háčikom.
- Farebná gradientová hlavička namiesto čiernej.
- Automatické rozpoznávanie všetkých S.M.A.R.T. problémov naraz.
- S.M.A.R.T. zobrazuje dni v prevádzke aj pri alerte.

## [2.1] - 2026-11-05

- Názov „Zdenko, zachráň ma!“.
- S.M.A.R.T. v Sprievodcovi: dni v prevádzke, všetky alerty, odporúčanie výmeny disku.
- Obnovenie prístupu je súčasťou Opravy, bez samostatného tlačidla.
- Stromová záchrana súborov a jedno tlačidlo „Zachrániť“.

## [2.0] - 2026-11-04

- Prvé vydanie: NTFS/FAT32 diagnostika, oprava, záchrana súborov, obrazy diskov a S.M.A.R.T.
