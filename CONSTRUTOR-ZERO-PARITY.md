# Construtor Zero — audyt funkcji i blokad wersji 3.0.1

Data: 2026-11-15. Ten dokument jest celowo restrykcyjny: **zgodna kompilacja nie oznacza pełnej zgodności w grze**.

## Port SnapBuilder (oryginał: https://github.com/toebeann/SnapBuilder)

| Moduł/funkcja | Stan w Construtor Zero |
|---|---|
| Przyciąganie pozycji na siatce | Implementacja uproszczona; symulowane testy pozytywne |
| Precyzyjne przyciąganie Ctrl | Implementacja uproszczona; symulowane testy pozytywne |
| Obrót 45° / Alt 5° | Tylko powierzchnie prawie poziome; symulowane testy pozytywne |
| Snap dowolnie obróconych powierzchni | Nieukończone |
| `AimTransform` i pełna matematyka położenia w relacji do celu | Nieukończone |
| `PlaceTool` (przedmioty trzymane w dłoni) | Nieukończone |
| Dopasowanie i podgląd dokładnych koliderów `ColliderRecord` | Nieukończone |
| `PhysicsPatch` / precyzyjne raycasty | Nieukończone |
| Podpowiedzi `BuilderTool.GetCustomUseText` i komplet skrótów | Nieukończone |
| Zasięg budowania | Częściowa implementacja; nie obejmuje pełnej oryginalnej ścieżki Targeting/Physics |

## Port Building Tweaks (oryginał: https://github.com/MrPurple6411/My-Subnautica-Mods)

| Oryginalna łatka | Stan |
|---|---|
| `BaseGhost_Finish_Patch` – przypinanie do parenta | **Zablokowana**: brak danych parenta w standardowym pakiecie budowania |
| `Base_BuildPillars_Patch` | Nieukończone |
| `Base_Start_Patch` i `Base_Update_Patch` | Nieukończone |
| `BuilderInputHandler_Patch` | Nieukończone |
| `Builder_CanDestroyObject_Patch` | Częściowa implementacja filtrowania |
| `Builder_GetSurfaceType_Patch` | Częściowe rozszerzenie dozwolonych powierzchni |
| `Builder_ObsticleChecks_Patches` | Łatki istnieją, **override zablokowany w MP** |
| `Builder_TryPlace_Patch` | Nieukończone, niebezpieczny oryginalny transpiler |
| `Builder_UpdateAllowed_Patch` | Częściowa implementacja |
| `Builder_Update_Patches` | Częściowe manualne pozycjonowanie, bez zgodności 1:1 |
| `Builder_ValidateOutdoor_Patch` | **Full Override zablokowany w MP** |
| `Constructable_Start_Patch` | Nieukończone |
| `DeconstructionAllowed_Patches` | **Override zablokowany w MP** |
| `Player_ResetPlayerOnDeath_Patch` | Nieukończone |
| `Player_Update_Patch` | Częściowe sterowanie, niepełna obsługa pojazdów |
| `PowerConsumer_IsPowered_Patch` | Uproszczona implementacja, symulowany test pozytywny |

## Craft From Containers BZ (bazowy kod: https://github.com/aedenthorn/SubnauticaMods)

| Funkcja | Stan |
|---|---|
| Rejestracja `StorageContainer` / czyszczenie po `OnDestroy` | Implementacja + symulowany test |
| Lista dozwolonych typów skrzyń | `Locker` i `SmallLocker`; inne typy **nieobsługiwane** |
| Zasięg (domyślnie 100 m) | Implementacja + symulowany test |
| Liczba składników przy podglądzie przepisu | **Nieaktywny w MP**: nie można obiecywać zasobów bez możliwości ich atomowego zużycia |
| `uGUI_RecipeEntry.UpdateIngredients` dla aktualnej wersji | Nieukończone |
| `Inventory.DestroyItem` | Tylko obserwacja; **nigdy nie zamienia false na true**, brak darmowego craftingu |
| Transfer do ekwipunku | Zaimplementowany eksperymentalny adapter zdarzenia `Storage.ItemRemoving`, wyłączony w konfiguracji |
| Oczekiwanie na zatwierdzenie transferu | Nieukończone |
| Automatyczne wznowienie craftingu i budowania po transferze | Nieukończone |
| Transakcje wieloskładnikowe, odrzucanie i rollback | Nieukończone |
| Równoczesne craftowanie przez dwóch klientów | Niezweryfikowane w grze |

## Dlaczego klientowe usuwanie przedmiotów jest zabronione

Oryginalny BepInEx Craft From Containers po nieudanym `Inventory.DestroyItem` wywołuje `ItemsContainer.DestroyItem` i zmienia wynik na `true`. To działa w singleplayerze, ale w BOT Benson multiplayer:

1. `Subnautica.Client/Synchronizations/Processors/Metadata/StorageProcessor.cs` usuwa ze skrzynki przez wysłanie `Metadata.StorageLocker` z `WorldPickupItem`.
2. `Subnautica.Server/Processors/Metadata/StorageProcessor.cs` waliduje przedmiot po stronie serwera i przeprowadza transfer do ekwipunku gracza.
3. Wynik transferu jest asynchroniczny; powrót `Inventory.DestroyItem` jest synchroniczny. Nie można bezpiecznie zastąpić go natychmiastowym `true` przy niepotwierdzonym transferze.
4. Serwer z publicznego repo nie udostępnia natywnej atomowej transakcji: `wiele zasobów ze skrzyń -> od razu zużyj na crafting`.

**Dokończenie wymaga** opóźnienia wykonywania przepisu do potwierdzenia transferów, obsługi odrzucenia, anulowania i ewentualnego rollbacku, a dla pełnej obsługi bez zajmowania ekwipunku – własnego protokołu serwerowego. Sam patch klienta byłby nieuczciwym potwierdzeniem craftingu.

## Testy i warunki wydania

- `src/IntegrationHarness` symuluje patche Harmony 2.3.3 oraz autorytatywne żądania transferu skrzynka -> gracz po konkretnym `ItemId`.
- Weryfikuje brak fałszowania liczby składników, brak usunięcia przed ack, blokowanie duplikatów, zasięg i allow-list.
- Nie symuluje prawdziwych DLL `Assembly-CSharp` ani całego BOT Benson Server 3.0.1 (publiczny kod serwera jest starszy).
- Do ostatecznej akceptacji wymagane są: rzeczywiste API aktualnych DLL gry, testy dwóch klientów z równoczesnym craftowaniem i budowaniem, wylogowanie, ponowne dołączenie i restart serwera, plus kontrola zapisów.

**Stan obecny: DEVELOPMENT / NOT READY TO TEST.** Nie scalać do main, nie publikować jako pełny port.

## Pochodzenie i licencje

- SnapBuilder: autor Tobey, LGPL-3.0, https://github.com/toebeann/SnapBuilder
- Building Tweaks: MrPurple6411, MIT, https://github.com/MrPurple6411/My-Subnautica-Mods
- Craft From Containers: aedenthorn / późniejsze porty społeczności, https://github.com/aedenthorn/SubnauticaMods oraz https://www.nexusmods.com/subnauticabelowzero/mods/695
- Przed publikacją pochodnych fragmentów źródeł należy zachować obowiązki LGPL-3.0 i MIT.

Nie umieszczaj w publicznym repo oryginalnych DLL Subnautica, multiplayer ani BepInEx.
