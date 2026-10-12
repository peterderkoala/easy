---
status: accepted
---
# Grading matrix in code with versioned criterion keys

The IHK grading matrix (sections, criteria, max points) is a constant in code, not a database table. Each criterion key carries the matrix version (e.g. `fiae-2026:3.2`), and stored examiner points refer to these keys. The IHK changes the matrix rarely and as a whole, and a table would need an admin editor and a migration of every stored grading the first time a criterion changes. Only the section weights are data (per cycle on `Cycle`, default in `ConfigEntry`), because admins adjust them.

## Consequences

- A new IHK matrix is a code change with a new version prefix. Old gradings keep their keys, and the code keeps every matrix version that stored points refer to.
- Renaming a key of a released version breaks stored points. Keys of a released version never change.
