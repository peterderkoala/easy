# Easy

Collaborative grading of IHK final exams by examiners. `CONCEPT.md` is the source of truth; this file only fixes the words.

## Language

### Documentation grading

**Documentation**:
The participant's written project work, one exam step of a participant in a cycle, graded in the split view. Its result is the documentation point source.
_Avoid_: practical exam (now means the oral exam), project report

**Grading matrix**:
The fixed IHK rubric for grading the documentation, and only the documentation: sections with weights, each split into criteria. One matrix for now (Fachinformatiker Anwendungsentwicklung).
_Avoid_: Bewertungsmatrix, rubric, grading sheet, evaluation scheme

**Section**:
One of the matrix's weighted parts (e.g. "Durchführung und Auftragsbearbeitung", 40 %). Its criteria add up to 10 points.
_Avoid_: area, category, comment section

**Criterion**:
The smallest gradable item of a section, with a maximum of integer points. An examiner gives it points and a remark.
_Avoid_: sub-criterion, Beurteilungspunkt, item

**Registration**:
An examiner's place as one of the two examiners of a participant's documentation, taken by the examiner themselves.
_Avoid_: assignment, Anmeldung

**Examiner grading**:
One examiner's points and remarks on every criterion for one participant's documentation. Blind by default; submitted when the examiner is done.
_Avoid_: grade (that is the stored point value per source), evaluation, rating

**Documentation score**:
The one value out of 100 that counts as the documentation point source: the rounded average of the submitted examiner gradings' totals.
_Avoid_: documentation grade, final points

**Third examiner**:
An examiner invited by name when the two registered examiners' totals differ beyond the threshold; their examiner grading joins the average.
_Avoid_: arbiter, Drittprüfer, referee

**Remark**:
An examiner's free text on one criterion, part of that examiner's grading.
_Avoid_: Bemerkung, note (a note is the German grade 1 to 6), comment
