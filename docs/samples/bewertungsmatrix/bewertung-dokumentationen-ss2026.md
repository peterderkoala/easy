---
title: Bewertung Dokumentationen SS2026 – Fine-grained scoring sheet (IHK Projektarbeit)
type: grading-rubric-detailed
language: de
source: Bewertung_Dokumentationen_SS2026.xlsx
sheets: [MUSTERBEWERTUNG, Teilnehmer Z (1 examiner), Teilnehmer X (2 examiners)]
related: bewertungsmatrix-projektarbeit-fiae.md (coarse version, same 5 sections and weights)
---

# Fine-grained scoring sheet

Each section (1–5) is scored 0–10 and broken into integer sub-criteria. The sub-points add up to the section's 10 points.

## Calculation rules

```
Section points   = SUM(sub-criteria points)            # max 10
Teilergebnis     = Section points × Gewichtung × 10    # max = weight × 100
Total            = SUM(Teilergebnisse)                 # max 100
Final (2 examiners) = ROUND((Total_1 + Total_2) / 2, 1)
Final (1 examiner)  = Total_1
```

## Grade scale (from the overview sheet of the full workbook)

| Total points | Note |
|---|---|
| 92–100 | 1 |
| 81–91 | 2 |
| 67–80 | 3 |
| 50–66 | 4 |
| 30–49 | 5 |
| 0–29 | 6 |

## Layout of one evaluation sheet

- Left block = Erstprüfer, right block = Zweitprüfer (identical structure).
- Header: `Prüfer`, `Prüflingsname`.
- Columns per block: Beurteilungspunkte (criterion) · Bemerkung (free text) · Punkte-Max · Punkte · Gewichtung · Teilergebnis.
- Section rows are formulas; only the sub-criteria `Punkte` and `Bemerkung` are filled in.
- If only one examiner rates, the right block stays empty.

## Criteria, max points and rules

### 1. Ausgangssituation (weight 15 %)

| Sub-criterion | Max |
|---|---|
| Projektziel, Teilaufgaben, Kundenwünsche | 5 |
| Projektumfeld, Projektschnittstellen | 5 |

Rules: Goals, sub-tasks and customer wishes must be stated in the documentation itself (not only by reference to the Antrag) and should be measurable (success/acceptance criteria). The environment must cover the organizational unit, roles, users, and technical and personnel interfaces.

### 2. Ressourcen- und Ablaufplanung (weight 10 %)

| Sub-criterion | Max |
|---|---|
| Ablaufplanung | 5 |
| Personal-, Sachmittel-, Terminplanung und ggf. Kostenplanung | 5 |

Rules: A recognizable plan (phase model, phases, order) separate from implementation. Time planning is broken down by phase. Personnel, material and cost planning are itemized and traceable (cost assumptions logical, e.g. amortization). Planning must not be mixed with the Soll-Ist comparison.

### 3. Durchführung und Auftragsbearbeitung (weight 40 %)

| Sub-criterion | Max |
|---|---|
| Prozessschritte, Vorgehensweise, Qualitätssicherung | 4 |
| Abweichung, Anpassung, Entscheidung | 3 |
| Komplexität der Aufgabenstellung | 3 |

Rules: Explain not only what was done but why (reasoning, alternatives, decisions). Cover design, implementation and QA (tests) with context; no bare code snippets. Deviations from the Antrag are described, including how they came about. Complexity must be visible in the documentation. Conditions (Auflagen) from the approved Antrag must be met; violations lower the score.

### 4. Projektergebnisse (weight 25 %)

| Sub-criterion | Max |
|---|---|
| Kommentierter Quellcode | 2 |
| Soll-Ist-Vergleich | 2 |
| Qualitätskontrolle | 2 |
| Abweichungen, Anpassungen | 2 |
| Benutzerdokumentation | 2 |

Rules:
- Source code: complete, commented, as an appendix (anonymized if needed). Excerpts only = partial points.
- Soll-Ist: detailed, covers requirements, time and costs, placed in the results part (not at the start).
- Quality control: tests described in detail (not only listed in a table).
- Deviations: sensibly described and justified.
- User documentation: a standalone guide or instructions. Screenshots alone are not enough. Missing without justification = 0.

### 5. Gestaltung der Dokumentation (weight 10 %)

| Sub-criterion | Max |
|---|---|
| Äußere Form | 5 |
| Inhaltliche Form | 5 |

Rules:
- Äußere Form: layout, numbering, cross-references, tables of contents/appendices, justified text, readable graphics, consistent presentation, balanced text vs. graphics/code ratio.
- Inhaltliche Form: clear structure with a common thread (roter Faden), correct spelling and grammar, technical language, sufficient content.

---

## Calibration examples (anonymized, from the sample sheets)

### Teilnehmer Z – one examiner

| Section | Sub-points | Section pts | Teilergebnis |
|---|---|---|---|
| 1 | 4 / 5 | 9 | 13.5 |
| 2 | 5 / 5 | 10 | 10.0 |
| 3 | 3 / 3 / 3 | 9 | 36.0 |
| 4 | 2 / 2 / 1 / 2 / 1 | 8 | 20.0 |
| 5 | 3 / 3 | 6 | 6.0 |
| **Total** | | | **85.5 → Note 2** |

Typical remarks: goal recognizable but without measurable success criteria; planning OK, cost assumptions logical; steps clear but "why" is sometimes missing; full source code; standalone user guide missing; formal weaknesses in numbering/appendix.

### Teilnehmer X – two examiners

| Section | Prüfer A | Prüfer B |
|---|---|---|
| 1 (3+3 / 3+2) | 6 → 9.0 | 5 → 7.5 |
| 2 (1+2 / 1+2) | 3 → 3.0 | 3 → 3.0 |
| 3 (2+1+1 / 2+1+1) | 4 → 16.0 | 4 → 16.0 |
| 4 (1+1+1+2+0 / 1+1+1+2+0) | 5 → 12.5 | 5 → 12.5 |
| 5 (5+2 / 3+3) | 7 → 7.0 | 6 → 6.0 |
| **Total** | **47.5** | **45.0** |

Final = ROUND((47.5 + 45.0) / 2, 1) = **46.3 → Note 5**.

Typical remarks: project justification and business unit missing; no phase model or real planning; cost planning only a few words; implementation described by code snippets only, no reasoning; Antrag conditions violated; source code only in excerpts; no user documentation; no common thread, confused numbering, no cross-references.

> Note: In the sample, Prüfer A gave Äußere Form 5 of 5 despite criticizing it in the remark, while Prüfer B gave 3. Examiners can differ here, so check consistency between remark and points.

## Agent usage

- To pre-score or review a documentation: go through every sub-criterion, assign integer points within the max, write a short remark (strengths, then gaps, with chapter references), then compute the section points, Teilergebnisse, Total and Note using the formulas above.
- Keep remarks factual and specific, in German, in the style of the examples (semicolon-separated findings).
