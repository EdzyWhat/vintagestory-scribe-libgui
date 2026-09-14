# Scribe locale glossary

Locked proper names (in-world nouns) for every shipped locale, so translators working in
parallel do not each invent their own Lectern. This is Decision 2 of
`openspec/changes/add-player-locales/design.md`. It covers only the nouns below — UI
action/button/tab labels are **not** hand-listed here; those are generated and enforced
mechanically by `build/check-locales.py`'s UI-label cross-reference check (see design.md
Decision 7).

Rules:

- Spanish (`es-es`) keeps C4B's existing names where C4B already translated that noun.
  Where C4B's pack predates a noun (Chalkboard, Assignment Desk, Inbox, Task Notice —
  1.4.x additions), the value below is the one to introduce, chosen to fit C4B's existing
  voice.
- Portuguese (`pt-br`) keeps Arquimago's existing names (confirmed from the current
  `pt-br.json`) where Arquimago already translated that noun. Same rule for the 1.4.x
  additions Arquimago's file predates.
- Greenfield languages (`ru`, `uk`, `pl`, `de`, `zh-cn`, `ja`, `cs`, `sv-se`, `fr`, `it`)
  each pick one natural equivalent and use it everywhere that noun appears — in the block/
  item name key, tab labels, and handbook prose alike.
- "Scribe" (the mod name) stays "Scribe" in every locale — no language here has an
  established alternate rendering, and the mod's own name should read as a name, not a
  translated common noun.
- "HUD" may stay as the bare loanword "HUD" (as English UI jargon commonly does even in
  translated software) rather than being translated as an initialism.

| English | es-es | pt-br | ru | uk | pl | de | zh-cn | ja | cs | sv-se | fr | it |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Scribe | Scribe | Scribe | Scribe | Scribe | Scribe | Scribe | Scribe | Scribe | Scribe | Scribe | Scribe | Scribe |
| Lectern | Atril (C4B) | Atril (Arquimago) | Пюпитр | Пюпітр | Pulpit | Lesepult | 讲台 | 書見台 | Pult | Pulpet | Lutrin | Leggio |
| Scriptorium | Escritorio (C4B) | Scriptorium | Скрипторий | Скрипторій | Skryptorium | Skriptorium | 缮写室 | 写字室 | Skriptorium | Skriptorium | Scriptorium | Scriptorium |
| Chalkboard | Pizarra | Lousa | Грифельная доска | Грифельна дошка | Tablica | Tafel | 黑板 | 黒板 | Tabule | Griffeltavla | Tableau noir | Lavagna |
| Assignment Desk | Escritorio de Asignaciones | Mesa de Atribuições | Стол поручений | Стіл доручень | Biurko Zleceń | Auftragspult | 任务分配台 | 任務割り当て台 | Přidělovací stůl | Uppdragspulpet | Bureau d'Attribution | Scrivania degli Incarichi |
| Inbox | Bandeja de Entrada | Caixa de Entrada | Входящие | Вхідні | Skrzynka odbiorcza | Posteingang | 收件箱 | 受信箱 | Doručená pošta | Inkorg | Boîte de réception | Posta in arrivo |
| Task Notice | Aviso de Tarea | Aviso de Tarefa | Уведомление о задании | Повідомлення про завдання | Zawiadomienie o zadaniu | Aufgabenmitteilung | 任务通知 | 任務通知 | Oznámení o úkolu | Uppgiftsmeddelande | Avis de Tâche | Avviso di Compito |
| Notebook | Cuaderno | Caderno (Arquimago) | Блокнот | Блокнот | Notatnik | Notizbuch | 笔记本 | ノート | Zápisník | Anteckningsbok | Carnet | Taccuino |
| Clockmaker's Notebook | Cuaderno del Relojero | Caderno do Relojoeiro (Arquimago) | Блокнот часовщика | Блокнот годинникаря | Notatnik Zegarmistrza | Uhrmacher-Notizbuch | 钟表匠笔记本 | 時計職人のノート | Zápisník hodináře | Urmakarens anteckningsbok | Carnet de l'Horloger | Taccuino dell'Orologiaio |
| Clay Tablet | Tablilla de Arcilla | Placa de Argila (Arquimago) | Глиняная табличка | Глиняна табличка | Tabliczka Gliniana | Lehmtafel | 泥板 | 粘土板 | Hliněná tabulka | Lertavla | Tablette d'Argile | Tavoletta di Argilla |
| Wax Tablet | Tablilla de Cera | Placa de Cera (Arquimago) | Восковая табличка | Воскова табличка | Tabliczka Woskowa | Wachstafel | 蜡板 | 蝋板 | Voskovaná tabulka | Vaxtavla | Tablette de Cire | Tavoletta di Cera |
| Guest Book | Libro de Visitas | Livro de Visitas (Arquimago) | Книга гостей | Книга гостей | Księga gości | Gästebuch | 访客留言簿 | 来訪者名簿 | Kniha hostů | Gästbok | Livre d'Or | Libro degli Ospiti |
| Pinned Task HUD | HUD de Tareas Fijadas | HUD de Tarefas Fixadas | HUD закреплённых задач | HUD закріплених завдань | HUD przypiętych zadań | Angepinnte-Aufgaben-HUD | 置顶任务HUD | ピン留めタスクHUD | HUD připnutých úkolů | HUD för fastnålade uppgifter | HUD des Tâches Épinglées | HUD delle Attività Appuntate |
| History | Historial | Histórico (Arquimago) | История | Історія | Historia | Chronik | 历史记录 | 履歴 | Historie | Historik | Historique | Cronologia |
| Transcribe | Transcribir | Transcrever | Копирование | Копіювання | Kopiowanie | Übertragen | 转录 | 転記 | Přepis | Överföring | Transcription | Trascrizione |

Notes for the ingest tasks (2.1 / 2.2):

- **es-es**: confirm every "(C4B)" cell against the actual extracted `es-es.json` before
  treating the table value as final — this table was drafted before ingest. If C4B's file
  uses a different word for a noun already in their pack, keep C4B's word and correct this
  table to match (do not overwrite C4B's established choice with the draft guess above).
  For nouns C4B's pack predates (Chalkboard, Assignment Desk, Inbox, Task Notice), the table
  value is the one to introduce.
- **pt-br**: "(Arquimago)" cells are confirmed from the current `pt-br.json` (Atril,
  Scriptorium not present — Arquimago's file predates it, so use the greenfield-style choice
  in the table). Chalkboard, Assignment Desk, Inbox, and Task Notice are 1.4.x additions
  Arquimago's file predates; the table value is the one to introduce.
- Bare wildcard keys (`block-chalkboard-*`, `block-scribeinbox-*`) resolve through these
  same nouns — the literal `*` stays in the key string, only the value is translated.
