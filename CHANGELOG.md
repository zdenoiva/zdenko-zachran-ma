# Zdenko, zachráň ma! — changelog

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
