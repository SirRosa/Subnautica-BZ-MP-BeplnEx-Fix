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
| Zasięg budowania | Zaimplementowano rozszerzenie `Builder` i `Targeting.GetTarget` wyłącznie w trakcie budowania, przechodzi testy symulacyjne; `PhysicsPatch` i pełna obsługa `PlaceTool` nadal brak |

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
| `Builder_TryPlace_Patch` | Nieprzeniesiony oryginalny transpiler; osobna bezpieczniejsza kolejka zasobów z prefiksem o priorytecie 1000 przed prefiksem sieciowym (test symulacyjny) |
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
| Rejestracja `StorageContainer` | `Awake` i `ResetContainer`, okresowe usuwanie zniszczonych obiektów Unity; `OnDestroy` nie istnieje w oryginalnej klasie |
| Lista dozwolonych typów skrzyń | `Locker` i `SmallLocker`; inne typy **nieobsługiwane** |
| Zasięg (domyślnie 100 m) | Implementacja + symulowany test |
| Liczba składników przy podglądzie przepisu | **Nieaktywny w MP**: nie można obiecywać zasobów bez możliwości ich atomowego zużycia |
| `uGUI_RecipeEntry.UpdateIngredients` | Własny observer do przypiętych przepisów i `TechData.GetIngredients`; nie zawyża liczb w UI |
| `Inventory.DestroyItem` | Tylko obserwacja; **nigdy nie zamienia false na true**, brak darmowego craftingu |
| Transfer do ekwipunku | Zaimplementowano adapter zdarzenia `Storage.ItemRemoving`, weryfikację przejęcia przez obsługę multiplayera, identyfikatorów i kolejkę; funkcje opt-in domyślnie wyłączone |
| Potwierdzenie transferu | Symulowana obsługa `Inventory.OnAddItem` sprawdza dokładny `ItemId`; niewłaściwy ACK i timeout odrzucane; brak testów sieci rzeczywistej |
| Automatyczne wznowienie craftingu | `AutoCraftCoordinator`: przechwycenie wyłącznie akcji `Craft`, ponowienie oryginalnej akcji po komplecie potwierdzonych surowców, anulowanie po zamknięciu menu — wyłączone domyślnie, test symulacyjny |
| Automatyczne wznowienie budowania | `AutoBuildCoordinator`: prefiks przed pakietem multiplayera, odtwarzanie standardowego `Builder.TryPlace` dopiero po dostawie i kontroli niezmienionej pozycji/rotacji, wyłączone domyślnie, test symulacyjny |
| Wieloskładnikowe operacje i rollback | Sekwencyjne transfery bez wyprzedzania ACK i bez duplikacji żądań; anulowanie oraz timeout. Nie ma atomowej transakcji wieloprzedmiotowej ani automatycznego zwrotu częściowo pobranych zasobów |
| Równoczesne craftowanie przez dwóch klientów | Niezweryfikowane w grze |

## Dlaczego klientowe usuwanie przedmiotów jest zabronione

Oryginalny BepInEx Craft From Containers po nieudanym `Inventory.DestroyItem` wywołuje `ItemsContainer.DestroyItem` i zmienia wynik na `true`. To działa w singleplayerze, ale w BOT Benson multiplayer:

1. `Subnautica.Client/Synchronizations/Processors/Metadata/StorageProcessor.cs` usuwa ze skrzynki przez wysłanie `Metadata.StorageLocker` z `WorldPickupItem`.
2. `Subnautica.Server/Processors/Metadata/StorageProcessor.cs` waliduje przedmiot po stronie serwera i przeprowadza transfer do ekwipunku gracza.
3. Wynik transferu jest asynchroniczny; powrót `Inventory.DestroyItem` jest synchroniczny. Nie można bezpiecznie zastąpić go natychmiastowym `true` przy niepotwierdzonym transferze.
4. Serwer z publicznego repo nie udostępnia natywnej atomowej transakcji: `wiele zasobów ze skrzyń -> od razu zużyj na crafting`.

**Zaimplementowano w trybie rozwojowym** odroczenie craftingu/budowania do rzeczywistego dostarczenia składników w ekwipunku. Nadal brakuje weryfikacji w grze, pełnego rollbacku po częściowej dostawie, obsługi wszystkich kontenerów oraz możliwości korzystania z surowców bez zajmowania miejsca w ekwipunku. Dla tej ostatniej funkcji niezbędne byłoby rozszerzenie serwera.

## Testy i warunki wydania

- `src/IntegrationHarness` symuluje patche Harmony 2.3.3 oraz autorytatywne żądania transferu skrzynka -> gracz po konkretnym `ItemId`.
- Weryfikuje brak fałszowania liczby składników, brak usunięcia przed ACK, blokowanie duplikatów, zasięg, allow-list, 2-etapowe potwierdzanie składników, anulowanie UI oraz pozę budowli i pierwszeństwo patcha przed nadawaniem pakietu multiplayera (99 testów).
- Nie symuluje prawdziwych DLL `Assembly-CSharp` ani całego BOT Benson Server 3.0.1 (publiczny kod serwera jest starszy).
- Do ostatecznej akceptacji wymagane są: rzeczywiste API aktualnych DLL gry, testy dwóch klientów z równoczesnym craftowaniem i budowaniem, wylogowanie, ponowne dołączenie i restart serwera, plus kontrola zapisów.

**Stan obecny: DEVELOPMENT / NOT READY TO TEST.** Nie scalać do main, nie publikować jako pełny port.

## Pochodzenie i licencje

- SnapBuilder: autor Tobey, LGPL-3.0, https://github.com/toebeann/SnapBuilder
- Building Tweaks: MrPurple6411, MIT, https://github.com/MrPurple6411/My-Subnautica-Mods
- Craft From Containers: aedenthorn / późniejsze porty społeczności, https://github.com/aedenthorn/SubnauticaMods oraz https://www.nexusmods.com/subnauticabelowzero/mods/695
- Przed publikacją pochodnych fragmentów źródeł należy zachować obowiązki LGPL-3.0 i MIT.

Nie umieszczaj w publicznym repo oryginalnych DLL Subnautica, multiplayer ani BepInEx.
