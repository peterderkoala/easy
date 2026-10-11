# Projektanträge Winter 2026/27 – Prüfungsübersicht (IHK-Import)

Quelle: `antrag_import.xlsx`, Sheet `Winter 202627`, 6 Datensätze (Zeile 3–8, Kopfzeile Zeile 2).

## Spalten-Schema

| Spalte | Feld | Herkunft | Bedeutung / Nutzung |
|---|---|---|---|
| A | Prüfungsteilnehmer/-in | IHK-Import | Vorname Nachname des Teilnehmers |
| B | Prüfl.-Nr. | IHK-Import | **Nicht Eindeutiger Schlüssel** je Antrag (Integer), steht im direkten Bezug zum Teilnehmer, kann in folgejahren erneut vergeben werden |
| C | Ausbildungsstätte | IHK-Import | Bildungsträger (ggf. `/ Backhaus IT` = Kooperationsbetrieb) |
| D | Status | IHK-Import | **Ignorieren.** Bei allen Zeilen identisch: „Antrag liegt dem Prüfungsausschuss zur Genehmigung vor." |
| E | Projektbezeichnung | IHK-Import | Titel des Projekts – Hauptinhalt für die inhaltliche Bewertung |
| F | Sprache | Prüfer (hat PDF gelesen) | Tatsächlich im Antrag-PDF genutzte Sprache/Technologie. Verlässlicher als jede IHK-Angabe |
| G | Kommentar | Prüfer (hat PDF gelesen) | Prüfergebnis und Auflagen, siehe Taxonomie unten |

**Vertrauenshierarchie:** F, G (menschlich aus PDF geprüft) > E, B, C (IHK-Import) > D (irrelevant).


