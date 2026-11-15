# Construtor Zero — stabilized release candidate for Below Zero MP 3.0.1

**v0.3.6-rc7: empty blueprints always place, wall plants adjust vertically, beds and floor lockers stay upright. CFC 100m ONLY applies to client-loaded lockers. REAL TWO-CLIENT RETEST REQUIRED.**
See [BLUEPRINT-ROTATION-301.md](BLUEPRINT-ROTATION-301.md) for current blueprint, wall aim, upright bed and CFC radius/streaming limitations. Prior rc6: [SURFACE-GRID-301.md](SURFACE-GRID-301.md), covering camera-pitch, wall priority and static world-grid fixes. Background on the prior floor/wall attachment work: [SURFACE-LOCK-301.md](SURFACE-LOCK-301.md). Prior rc4 UI and CFC performance changes: [PLACEMENT-UI-301.md](PLACEMENT-UI-301.md). Previous rc3 context: [GAMEPLAY-FIXES-301.md](GAMEPLAY-FIXES-301.md); background in [STABILIZATION-301.md](STABILIZATION-301.md) for verified safeguards, installation, limitations and mandatory two-client acceptance tests. This branch integrates
SnapBuilder and Building Tweaks porting work with the safe starting point for
Craft From Containers. Full multiplayer feature parity is **not implemented**.

Read the exhaustive [feature parity and network limitations](CONSTRUTOR-ZERO-PARITY.md)
before assuming a CI success is game compatibility.

The experimental Craft From Containers module NEVER turns an unconfirmed
storage transfer into a successful recipe consumption. This protects against
free crafting/duplication. **Newly implemented but disabled by default:**
server-acknowledged sequential prefetch, deferred fabricator Craft action,
and **nonblocking** ingredient prefetch on blueprint placement. Builder.TryPlace
must NEVER be deferred, suppressed or replayed on resource availability; an
empty construction blueprint can be placed with zero materials.
These features passed only a simulated Unity/Harmony/MP integration harness;
they have NOT been verified on a real 3.0.1 server or two game clients.

Tests now simulate BOT Benson's construction Harmony prefixes, proving
a blueprint construction packet still goes out even with zero resources,
and later server resource ACKs cannot replay that placement. Regression fixes cover duplicate recipe ingredient rows, fast repeat-click races,
and restoring temporary Builder surface permissions after construction.
The original SnapBuilder and Building Tweaks are still only partially ported; arbitrary
moving-parent attachment is not supported without a new server protocol.

Code: [Construtor Zero source](src/ConstrutorZero)
  | [ABI audit](ABI-AUDIT-301.md)
  | [feature parity status](CONSTRUTOR-ZERO-PARITY.md).

New in rc2: an unexpected Harmony/Unity exception disables CFC and clears pending operations rather than breaking Player.Update; furniture auto-build preflights full local ingredient availability before any network transfer. CI verifies these failure paths; the server is still authoritative.

Test coverage now includes real-ABI-based fabricator UI gating, exact ItemId + TechType acknowledgement, UI deselection, race and disconnect cancellation, stale-locker quarantine, base/furniture flag restoration, geometry rollback and wall/ceiling snap. CI pass counts are reported by the final workflow run, not treated as a gameplay certification.

---

# Subnautica Below Zero Multiplayer 3.0.1 — native plugin ports

Repozytorium eksperymentalnych wtyczek do multiplayera BOT Bensona **bez BepInEx i Nautilus**.

## Potwierdzone

- Użytkownik potwierdził wykonanie `Subnautica.BZ.MP.PortProbe.OnEnabled()` w 3.0.1: `[Sirro.MP.PortProbe] enabled`. Loader obsługuje lokalną DLL nawet gdy nie widać jej na liście launchera.
- Oryginalny kod i statyczne zależności trzech modów przeanalizowano. Szczegóły: [RESEARCH.md](RESEARCH.md).
- Build GitHub Actions kompiluje wtyczkę względem minimalnego kontraktu `Subnautica.API`, bez publikowania bibliotek gry.

## Etap 1 — Native SnapBuilder 0.1 (**eksperymentalny, nie pełny port**)

`src/NativeSnapBuilder`: osobna wtyczka `Sirro.BZ.MP.NativeSnapBuilder.dll`.

- Próbuje włączyć Harmony Postfix dla `Builder.SetPlaceOnSurface`.
- Przyciąga do siatki na płaszczyźnie powierzchni w lokalnych współrzędnych trafionego obiektu; pomija całe części baz (`ConstructableBase`).
- Siatka domyślnie `0.5` m, przy przytrzymaniu lewego/prawego Control `0.1` m; kliknięcie środkowym przyciskiem myszy (`Mouse2`) przełącza funkcję.
- Konfiguracja: `%APPDATA%\\.botbenson\\Subnautica Below Zero\\Game\\Config\\Sirro.NativeSnapBuilder.ini`.
- Diagnostyka: wpisy `[Sirro.NativeSnapBuilder] ENABLED` lub `DISABLED: ...` w logu multiplayera.

**Ograniczenia:** nie ma jeszcze obrotu co 45°/5°, poprawionych kolizji, suwaków zasięgu ani UI. Refleksyjne podłączenie Harmony i zachowanie w czasie rzeczywistej gry **nie są jeszcze potwierdzone**. GitHub Actions weryfikuje kompilację i samą matematykę siatki — nie uruchamia Subnautica Below Zero.

**Bezpieczny test:** sprawdź osobną, zapasową kopię zapisu. Nie testuj konstrukcji na wspólnym zapisie produkcyjnym, dopóki oba klienty nie będą poprawnie widzieć tej samej pozycji, a stan nie będzie przetrwał ponownego dołączenia. Usuń DLL, jeśli wystąpią błędy synchronizacji.

## Pozostałe porty

- Building Tweaks: funkcje przebudowane bez Nautilus; szczególnie ostrożnie z modyfikacjami hierarchii obiektów i walidacji umieszczania.
- Craft From Containers: zasoby ze skrzyń muszą być zużywane autorytatywnie po stronie hosta, aby nie dochodziło do duplikacji.

Oryginalne DLL i biblioteki komercyjnej gry nie są umieszczane w repozytorium.

Źródło inspiracji i porównania zachowania SnapBuilder: https://github.com/toebeann/SnapBuilder (LGPL-3.0).
