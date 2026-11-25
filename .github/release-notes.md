Planificator de orar universitar (C#, .NET 10, WPF) — auditorii, discipline, profesori, grupe și sarcina didactică.

### Descărcare și pornire
1. Descărcați arhiva **`OrarStudio-…-win-x64.zip`** (mai jos, la *Assets*) și dezarhivați-o.
2. Porniți **`OrarStudio.exe`** — nu necesită instalarea .NET (Windows 10/11, 64 de biți).
3. La prima pornire se deschide un proiect gol, iar panoul de control arată primii pași: importați sarcina didactică (Ctrl+I), adăugați auditoriile și generați orarul (Ctrl+G).

### Ce conține
- **Date**: auditorii (destinație curs / seminar / laborator, capacitate, dotări), discipline, profesori cu doleanțe, grupe cu subgrupe.
- **Import** din fișierele departamentelor (`.xlsx`, `.docx`, `.csv`), cu previzualizare editabilă și detectarea doleanțelor.
- **Editor de orar**: orarul unei grupe, al unui profesor sau al unui auditoriu ori vederea de ansamblu „Toate grupele / Toți profesorii / Toate auditoriile”; drag & drop cu verificarea intervalelor în timp real, dublu-clic pentru editare, meniu contextual, Ctrl+Z / Ctrl+Y, săptămâni pare/impare, subgrupe, cursuri pe serii.
- **Generare automată** fără conflicte (profesor, grupă, auditoriu, capacitate, doleanțe).
- **Export**: PDF (cu previzualizare), Excel, CSV, iCalendar, JSON, site static.
- **Web & API**: server REST integrat, site public al orarului, panou web (`/board`), documentație (`/docs`).
- **Panou informativ** pentru ecranele de pe hol (F11): ce are loc acum și ce urmează.
- Temă luminoasă / întunecată; 32 de cazuri de test automate.

Arhiva este construită și testată automat de GitHub Actions din codul acestui tag. Detalii în [README](https://github.com/ProiectDeSemestru/OrarStudio#readme).
