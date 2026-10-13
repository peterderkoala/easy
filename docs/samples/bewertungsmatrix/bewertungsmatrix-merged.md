---
title: Bewertungsmatrix Projektarbeit – Fachinformatiker/in Anwendungsentwicklung (IHK) – merged
type: grading-rubric
language: de
sources:
  - Bewertungsmatrix_Projektarbeit.pdf (coarse matrix)
  - Bewertung_Dokumentationen_SS2026.xlsx (fine-grained scoring sheet)
scale: 0-10 points per section, 0-100 total
---

# Bewertungsmatrix Projektarbeit – FI Anwendungsentwicklung

Two views of one rubric: the PDF defines sections, criteria and weights. The Excel sheet breaks each section into sub-criteria with max points and calculates the final score and grade.

## 1. Meta and sign-off

- Candidate: Prüflingsnummer/-name (filled by the examiners)
- Raters: Erstprüfer, Zweitprüfer (each gives remarks + points)
- Sign-off (PDF): date + signature of both raters
- Excel layout: left block = Erstprüfer, right block = Zweitprüfer (same structure). Columns: Beurteilungspunkte · Bemerkung · Punkte-Max · Punkte · Gewichtung · Teilergebnis. Only sub-criteria `Punkte` and `Bemerkung` are filled in. Section rows are formulas. A single examiner leaves the right block empty.

## 2. Calculation

```
Section points      = SUM(sub-criteria points)            # max 10
Teilergebnis        = Section points × Gewichtung × 10    # max = weight × 100
Total               = SUM(Teilergebnisse)                 # max 100
Final (2 examiners) = ROUND((Total_1 + Total_2) / 2, 1)
Final (1 examiner)  = Total_1
```

### Grade scale (from the overview sheet of the full workbook)

| Total points | Note |
|---|---|
| 92–100 | 1 |
| 81–91 | 2 |
| 67–80 | 3 |
| 50–66 | 4 |
| 30–49 | 5 |
| 0–29 | 6 |

## 3. Sections, weights, sub-criteria

| # | Section | Weight | Sub-criteria (max points) |
|---|---|---|---|
| 1 | Ausgangssituation | 15 % | Projektziel, Teilaufgaben, Kundenwünsche (5) · Projektumfeld, Projektschnittstellen (5) |
| 2 | Ressourcen- und Ablaufplanung | 10 % | Ablaufplanung (5) · Personal-, Sachmittel-, Terminplanung und ggf. Kostenplanung (5) |
| 3 | Durchführung und Auftragsbearbeitung | 40 % | Prozessschritte, Vorgehensweise, Qualitätssicherung (4) · Abweichung, Anpassung, Entscheidung (3) · Komplexität der Aufgabenstellung (3) |
| 4 | Projektergebnisse | 25 % | Kommentierter Quellcode (2) · Soll-Ist-Vergleich (2) · Qualitätskontrolle (2) · Abweichungen, Anpassungen (2) · Benutzerdokumentation (2) |
| 5 | Gestaltung der Dokumentation | 10 % | Äußere Form (5) · Inhaltliche Form (5) |

> The PDF shows an alternative weighting for sections 4/5 (20 % / 15 % instead of 25 % / 10 %) without saying when it applies, probably chamber-specific. The Excel sheet uses 25 % / 10 %. Confirm with your chamber (IHK) before using the alternative.

## 4. Rules per section

### 1. Ausgangssituation (15 %)

- **Projektziel, Teilaufgaben, Kundenwünsche (5):** Goals, sub-tasks and customer wishes are stated in the documentation itself, not only by reference to the Antrag. Goals should be measurable (success/acceptance criteria).
- **Projektumfeld, Projektschnittstellen (5):** Describe the environment: organizational unit, roles, users, contacts, inputs/outputs. Cover both technical and personnel interfaces.

### 2. Ressourcen- und Ablaufplanung (10 %)

- **Ablaufplanung (5):** A recognizable plan (phase model, phases, order), separate from implementation.
- **Personal-, Sachmittel-, Terminplanung, ggf. Kosten (5):** Time planning is broken down by phase. Personnel, material and costs are itemized and traceable, with logical cost assumptions (e.g. amortization). Planning is not mixed with the Soll-Ist comparison.

### 3. Durchführung und Auftragsbearbeitung (40 %)

Highest weight.

- **Prozessschritte, Vorgehensweise, Qualitätssicherung (4):** Explain not only what was done but why (reasoning, alternatives). Cover design, implementation and QA (tests) with context. Bare code snippets do not count.
- **Abweichung, Anpassung, Entscheidung (3):** Deviations from the Antrag are described, including how they came about and which decisions were taken.
- **Komplexität der Aufgabenstellung (3):** The complexity must be visible in the documentation. Conditions (Auflagen) from the approved Antrag must be met. Violations lower the score.

### 4. Projektergebnisse (25 %)

- **Kommentierter Quellcode (2):** Complete and commented, as an appendix (anonymized if needed). If not possible, a justification is required. Excerpts only earn partial points.
- **Soll-Ist-Vergleich (2):** Detailed, covers requirements incl. tests, time and costs. It belongs in the results part, not at the start.
- **Qualitätskontrolle (2):** Quality (maintainability, reusability) and tests are described in detail, not only listed in a table.
- **Abweichungen, Anpassungen (2):** Sensibly described and justified.
- **Benutzerdokumentation (2):** A standalone guide or instructions. Screenshots alone are not enough. Missing without justification = 0.

### 5. Gestaltung der Dokumentation (10 %)

- **Äußere Form (5):** Layout of text, tables and graphics. Numbering, cross-references, tables of contents/appendices, justified text, readable graphics, consistent presentation, balanced ratio of text to graphics/code.
- **Inhaltliche Form (5):** Clear structure with a common thread (roter Faden), technically correct and standards-compliant presentation, correct spelling and grammar, technical language, sufficient content.

## 5. Calibration examples (anonymized)

### Teilnehmer Z – one examiner

| Section | Sub-points | Section pts | Teilergebnis |
|---|---|---|---|
| 1 | 4 / 5 | 9 | 13.5 |
| 2 | 5 / 5 | 10 | 10.0 |
| 3 | 3 / 3 / 3 | 9 | 36.0 |
| 4 | 2 / 2 / 1 / 2 / 1 | 8 | 20.0 |
| 5 | 3 / 3 | 6 | 6.0 |
| **Total** | | | **85.5 → Note 2** |

Typical remarks: goal recognizable but without measurable success criteria; planning OK, cost assumptions logical; steps clear but "why" sometimes missing; full source code; standalone user guide missing; formal weaknesses in numbering/appendix.

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

Typical remarks: project justification and business unit missing; no phase model or real planning; cost planning only a few words; implementation described by code snippets without reasoning; Antrag conditions violated; source code only in excerpts; no user documentation; no common thread, confused numbering, no cross-references.

> In the sample, Prüfer A gave Äußere Form 5/5 despite a critical remark, while Prüfer B gave 3. Check consistency between remark and points.

## 6. Agent usage

### Self-review of a project documentation

- [ ] Goals, sub-tasks, customer wishes, measurable success criteria, environment, contacts and interfaces stated (1)
- [ ] Phase model, schedule per phase, personnel/material/cost planned, no Soll-Ist mixed in (2)
- [ ] Method with reasoning, QA, deviations + decisions, complexity visible, Antrag conditions met (3)
- [ ] Complete commented code, Soll-Ist incl. tests/time/costs, detailed quality control, standalone user docs (4)
- [ ] Clean layout, numbering, cross-references, common thread, correct language (5)

### Pre-scoring or reviewing

1. Go through every sub-criterion, assign integer points within the max, and write a short remark (strengths first, then gaps, with chapter references).
2. Compute section points, Teilergebnisse, Total and Note with the formulas in section 2.
3. Write remarks in German, factual and specific, in the style of the examples (semicolon-separated findings).
