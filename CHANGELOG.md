# Zdenko, zachráň ma! — changelog

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
