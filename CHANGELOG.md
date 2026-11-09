# Zdenko, zachráň ma! — changelog

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
