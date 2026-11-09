# Zdenko, zachráň ma!

**Verzia 2.7** · © 2026 **Zdenko Ivančík**

Nekomerčné použitie je povolené. **Komerčné použitie je možné len s výslovným
súhlasom autora.**

Windows aplikácia na záchranu a opravu diskov. Zameraná na **NTFS** a **FAT32**.
Program sám zistí, čo sa na disku nachádza, a navrhne ďalší krok. Nič nemení bez
potvrdenia.

- GitHub: <https://github.com/zdenoiva/zdenko-zachran-ma>
- Releases: <https://github.com/zdenoiva/zdenko-zachran-ma/releases>
- Stránka na stiahnutie: <https://zdenoiva.github.io/zdenko-zachran-ma/>
- Aktuálna verzia: **2.7**

## Inštalácia alebo prenosné spustenie

- **Prenosná verzia:** `dist\ZachranaDisku.exe`
  - **jeden súbor**, self-contained, nepotrebuje .NET ani inštaláciu
  - skopírujte ho na iný počítač a spustite
  - všetky potrebné natívne knižnice sa rozbalia dočasne automaticky
- **Inštalátor:** `dist\ZachranaDisku-Setup-2.7.exe`
  - po spustení ponúkne dve možnosti:
    - **Nainštalovať do počítača** (Program Files, Štart, odinštalovanie cez
      Nastavenia → Aplikácie)
    - **Spustiť bez inštalácie** – rozbalí `ZachranaDisku.exe` vedľa
      inštalátora (alebo do `%LOCALAPPDATA%\ZachranaDisku\portable`) a hneď
      ho spustí
- Pri prvom spustení program overí prostredie a ponúkne automatické doinštalovanie.

### Aktualizácie
- program pri spustení asynchrónne overí `latest.json` na GitHube
- ak je dostupná novšia verzia, zobrazí sa tenká lišta s odkazom na stránku
  na stiahnutie
- kontrola je len informatívna; nič sa neinštaluje automaticky
- manuálna kontrola: **O programe → Skontrolovať aktualizácie…**

## Čo program vie

### Automatická detekcia
- zobrazí fyzické disky a **všetky partície**, aj bez písmena
- prečíta MBR/GPT, offset a veľkosť partície
- rozpozná **NTFS**, **FAT32**, **exFAT** (len označí) a **RAW**
- pri poškodenom primárnom boot sectore skúsi záložný boot sector
- pri RAW sa opýta: **NTFS / FAT32 / Neviem**; pri „Neviem“ vyskúša oba
  parsery len na čítanie

### Sprievodca
- rýchla diagnostika bez zápisu:
  - NTFS: boot sector, `$MFT`, koreňový adresár, dirty bit, stav Windows
  - FAT32: boot sector, záložný boot sector, FAT1/FAT2, root adresár
  - **S.M.A.R.T.**: vždy počet **dní v prevádzke**; pri probléme vypíše **všetky**
    dôvody naraz (teplota, vadné/nestabilné sektory, opotrebenie, chyby čítania
    aj zápisu) a pri vážnych príčinách aj **„Odporúčaná výmena disku“**
- veľká karta s verdiktom a **jedným odporúčaným krokom**
- ďalšie kroky sa zobrazia podľa výsledku:
  - skopírovať súbory
  - opraviť (`chkdsk`)
  - obnoviť boot sector
  - obnoviť FAT1 z FAT2
  - hĺbková záchrana
  - priradiť dočasné písmeno

### Záchrana súborov (len čítanie)
- **stromová štruktúra**: adresáre sa dajú rozbaľovať, zaškrtávať a zachrániť
- jedno tlačidlo **Zachrániť**; cieľový priečinok si vyberáte vy
- NTFS: MFT, `$FILE_NAME`, runlisty, fragmentácia, `$ATTRIBUTE_LIST`,
  resident/non-resident, sparse, LZNT1, zmazané záznamy
- FAT32: boot sector, FAT1/FAT2, rekurzívne adresáre, LFN aj 8.3,
  zmazané záznamy (`0xE5`), reťazce klastrov, bad clusters, cykly
- ak FAT chýba: súvislé čítanie od prvého klastra (fragmentované súbory
  môžu byť neúplné, program na to upozorní)
- kopírovanie so zachovaním stromu, resume, `.part` súbory
- ak je veľkosť súboru neznáma (0), kontrola veľkosti sa **preskočí a zapíše
  do logu**; súbor sa zachráni
- cieľ musí byť na inom fyzickom disku
- **na zdrojový disk sa nikdy nezapisuje**

### Obraz disku
- **Vytvoriť obraz** v hlavičke: celý disk alebo iba partícia
- `.part` súbor + pokračovanie po prerušení
- neprečítateľné sektory sa nahradia nulami a zapíšu do `.bad.txt`
- **Otvoriť obraz**: program rozpozná GPT/MBR aj partičný obraz
  a ponúkne jeho partície na záchranu

### Obnovenie prístupu
- **je súčasťou Opravy**; samostatné tlačidlo nie je potrebné
- po `chkdsk` program automaticky overí prístup a pri probléme s ACL spustí
  `takeown` + `icacls`
- ak Windows volume nenamontuje (`Access Denied`), program vie:
  - odpojiť a znova pripojiť volume (`mountvol`)
  - priradiť/odobrať dočasné písmeno
  - obnoviť ACL na koreni
- toto je oddelené od `chkdsk`; často práve to vyrieši „Access Denied“

### Hĺbkový NTFS sken
- hľadá `FILE` záznamy priamo na partícii, aj keď je `$MFT` poškodené
- podporuje record size 1024 aj 4096
- použije číslo záznamu z hlavičky alebo z MFT fragment mapy
- rekonštruuje strom z `$FILE_NAME`, osirotené súbory do `__OSIROTENÉ__`
- ukladá **resume index**, takže prerušený sken môže pokračovať
- na extrakciu používa ten istý čítač ako bežný NTFS (LZNT1, sparse, atď.)

### Oprava
- NTFS: záloha metadát + `chkdsk /f /x /v` + `Repair-Volume` + overenie
- **po chkdsk program automaticky overí prístup**; ak je problém v ACL, spustí
  `takeown` + `icacls` a znovu skontroluje
- FAT32: záloha boot sectoru/FAT + obnova boot sectoru zo sektora 6 +
  obnova FAT1 z FAT2 + `chkdsk /f` + overenie
- ak partícia nemá písmeno, program sa pokúsi **dočasne priradiť voľné
  písmeno** cez PowerShell a po oprave ho odoberie
- systémové/recovery partície program odmieta opravovať

### Boot — oprava štartu Windows
- samostatná záložka **Boot** pre celý disk
- detekcia **UEFI/BIOS** a **GPT/MBR**
- UEFI: ESP, `bootmgfw.efi`, `\EFI\Boot\bootx64.efi`, BCD
- BIOS: active partícia, `bootmgr`, MBR boot code, VBR, disk signature
- oprava:
  - UEFI: `bcdboot /f UEFI`
  - BIOS: `bcdboot /f BIOS` + `bootsect /nt60 /mbr`
- pred zápisom sa vždy uloží záloha BCD, boot súborov, MBR a VBR
- **aktuálny systémový disk je pre opravu zablokovaný**; použiť treba iný disk
  alebo WinRE
- voliteľná **konverzia MBR → GPT** cez `mbr2gpt` s kontrolou a varovaním
- pridanie **Windows RE / OEM recovery WIM** do boot menu
- samostatný popis nastavení **BIOS/UEFI** vrátane rizika SATA/RAID/AHCI radičov

### Protokol a zálohy
- všetky operácie sa logujú do `%LOCALAPPDATA%\ZdenkoZachranMa\logs`
- zálohy metadát: `Dokumenty\ZdenkoZachranMa\zalohy`
- inventár súborov: `Dokumenty\ZdenkoZachranMa\inventar`

### Prvé spustenie a kontrola prostredia
- program pri prvom spustení **ticho overí** Windows 10/11 x64, administrátorské
  práva, PowerShell 5.1, Storage cmdlety, `chkdsk`, `mountvol`, `takeown`,
  `icacls` a `shutdown`
- ak je všetko v poriadku, **žiadne okno sa nezobrazí**
- ak niečo chýba, program to **automaticky doinštaluje** (DISM/SFC) a zobrazí
  len informatívne okno, ktoré sa po úspechu samo zavrie
- všetko sa deje priamo v programe; nie je potrebné nič hľadať na webe
- kontrolu je možné kedykoľvek zopakovať cez **O programe → Skontrolovať prostredie…**

### Systémové súbory Windows (Diagnóza)
- tlačidlo **Skontrolovať systémové súbory (DISM)** spustí
  `DISM /Online /Cleanup-Image /CheckHealth`
- ak DISM nájde poškodenie, program hneď ponúkne opravu
- oprava spustí `DISM /Online /Cleanup-Image /RestoreHealth` a potom
  `SFC /scannow`
- pri požiadavke na reštart program na to upozorní

## Ako to používať

1. Pripojte disk priamo do PC (nie cez hub).
2. Spustite `ZachranaDisku.exe` ako administrátora
   (alebo použite nainštalovanú verziu).
3. V Sprievodcovi vyberte disk/partíciu.
4. Prečítajte si verdikt a odporúčanie.
5. Ak sú dáta dôležité, najprv **Skopírovať súbory** na iný disk.
6. Až potom **Opraviť**.
7. Po oprave sa diagnostika obnoví.

## Zostavenie

Potrebné: Windows 10/11 x64 a .NET 10 SDK.

```powershell
cd NtfsRescue
.\build.ps1
```

Výsledok: `dist\ZachranaDisku.exe` (self-contained, jeden súbor).

Inštalátor (vyžaduje Inno Setup 6):

```powershell
winget install JRSoftware.InnoSetup
.\make-installer.ps1
```

Výsledok: `dist\ZachranaDisku-Setup-2.7.exe`.

## Vydanie novej verzie

1. Zvýšte verziu v `Util/AppInfo.cs`, `NtfsRescue.csproj`, `app.manifest`
   a `installer.iss`.
2. Doplňte `CHANGELOG.md`.
3. Spustite:

```powershell
.\release.ps1
```

Skript:
- postaví prenosnú verziu aj inštalátor,
- vypočíta SHA256 a veľkosti,
- aktualizuje `latest.json`,
- vytvorí alebo aktualizuje GitHub Release `v<verzia>` a nahrá oba súbory.

Potom commitnite a pushnite `latest.json` a `CHANGELOG.md`:

```powershell
git add latest.json CHANGELOG.md
git commit -m "Release v2.7"
git push
```

GitHub Pages publikuje obsah priečinka `docs/`. Stránka číta `latest.json`
priamo z vetvy `main`, takže nová verzia sa na nej zobrazí hneď po pushi.

## Test bez disku

```powershell
dotnet .\bin\Debug\net10.0-windows\ZachranaDisku.dll --selftest
type $env:TEMP\ZdenkoZachranMa-selftest.log
```

Testuje NTFS boot sector, runlist, MFT záznam s fixupom, LZNT1 a syntetický
FAT32 obraz. Nepotrebuje administrátora a nepristupuje k fyzickému disku.

## Čo program zatiaľ neobsahuje

- **zápis opravy `$MFT` record 0** — hĺbkový sken je hotový, samotný zápis
  je pripravený na ďalšiu fázu (vyžaduje veľmi opatrné testovanie)
- **exFAT záchrana** — zatiaľ len detekcia
- **carving** (PhotoRec a pod.) — kvôli licenciám sa nepoužíva
- **GPL/AGPL/CPL nástroje** — nepoužívajú sa

## Použité nástroje a autori

- **Windows chkdsk** — Microsoft
- **PowerShell Storage cmdlets** — Microsoft (dočasné písmeno, stav volume)
- **rust-ntfs-recover** (0BSD) — inšpirácia pre algoritmus hĺbkového NTFS skenu, autor **mjgil-rust**
- **baize** (MIT) — inšpirácia pre trojvrstvový koncept, autor **karminski**
- **Microsoft FAT specification** — verejná dokumentácia pre FAT32 parser
- vlastný kód — **Zdenko Ivančík**

Nepoužité: TestDisk/PhotoRec (GPL), RecuperaBit (GPL), The Sleuth Kit (CPL),
ntfs-3g (GPL), dfir_ntfs (GPL), dissect.ntfs (AGPL), ntfstool (MIT, voliteľný).

Licenčné texty tretích strán: `THIRD_PARTY_NOTICES.md`.

## Upozornenie

Program pracuje s diskovými zariadeniami a pri oprave zapisuje do metadát.
Používate ho na vlastné riziko. Pred opravou si vždy skopírujte dôležité dáta.
Záchrana má zmysel len dovtedy, kým sa prepísané dáta neprepíšu inými.





