# Zdenko, zachráň ma! — changelog

## [2.5] - 2026-11-09

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
