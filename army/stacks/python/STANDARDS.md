# Standards Python

> Lu par `software-architect-python`, `developer-python` et `code-reviewer` sur du code Python. Les conventions existantes du dépôt priment : signaler l'écart, ne pas tout réécrire.

## 1. Socle
- **Version** : dernière version stable supportée par toutes les dépendances (ex. Python 3.13+), fixée dans `.python-version` et `pyproject.toml` (`requires-python`).
- **Gestion du projet** : `uv` (dépendances, venv, lockfile `uv.lock`) ; alternative Poetry si déjà en place.
- **Qualité** : `ruff` (lint + format), `mypy --strict` ou `pyright` en mode strict, `pre-commit`.
- Tout est déclaré dans `pyproject.toml` (pas de `setup.py`, pas de `requirements.txt` non généré).

## 2. Architecture de référence
Layout `src/` + architecture hexagonale pour un domaine riche ; modules par fonctionnalité pour un service simple.
```
src/my_app/
├── domain/           entités (dataclasses / Pydantic sans I/O), règles, exceptions métier
├── application/      services / use cases, ports (typing.Protocol)
├── infrastructure/   repositories SQLAlchemy, clients HTTP, messaging
├── api/              FastAPI routers, schémas Pydantic d'entrée/sortie, dépendances
├── config.py         pydantic-settings
└── main.py           composition root
tests/
├── unit/
├── integration/
└── conftest.py
```
- Règles d'import vérifiées par **import-linter**.

## 3. Choix techniques par défaut
| Besoin | Choix par défaut | Alternatives |
|---|---|---|
| API HTTP | FastAPI | Django + DRF (back-office riche, admin), Litestar |
| Validation / sérialisation | Pydantic v2 | msgspec (perf) |
| Configuration | pydantic-settings | — |
| ORM | SQLAlchemy 2.x (style typé, async si I/O intensif) | Django ORM, SQLModel |
| Migrations | Alembic | Django migrations |
| Client HTTP | httpx (async) | requests (sync, scripts) |
| Tâches asynchrones | Celery / Dramatiq / arq | — |
| Data | Polars, pandas, DuckDB | PySpark (gros volumes) |
| Logs | `structlog` ou `logging` en JSON + OpenTelemetry | loguru |
| Tests | pytest, pytest-asyncio, pytest-cov, Hypothesis, Testcontainers, respx | — |

## 4. Conventions de code
- PEP 8 via ruff ; `snake_case` fonctions/variables, `PascalCase` classes, `UPPER_CASE` constantes.
- **Annotations de type obligatoires** sur toute fonction publique ; pas de `Any` non justifié.
- `dataclass(frozen=True, slots=True)` pour les value objects.
- Ports définis par `typing.Protocol` ; injection via paramètres ou `Depends` FastAPI.
- Pas d'état global mutable ; pas d'I/O à l'import d'un module.
- Exceptions métier dédiées héritant d'une base `DomainError` ; mapping vers HTTP dans la couche API uniquement.
- `pathlib` plutôt que `os.path` ; f-strings ; `datetime` toujours *timezone-aware* (UTC).
- async : ne jamais appeler de code bloquant dans une coroutine (utiliser `asyncio.to_thread`).

## 5. Sécurité
- Secrets via variables d'environnement injectées par le coffre-fort ; jamais de `.env` commité.
- Requêtes SQL paramétrées (SQLAlchemy) ; jamais de f-string dans du SQL.
- Interdits : `eval`, `exec`, `pickle` sur des données non fiables, `yaml.load` sans `SafeLoader`, `subprocess(shell=True)` avec entrée utilisateur.
- Scans : `pip-audit` (ou `uv` audit), `bandit` (ou règles `S` de ruff).

## 6. Tests
- Nommage : `test_<comportement>_<condition>`.
- Fixtures pytest pour l'injection ; factories (factory_boy / polyfactory).
- Tests d'intégration sur vraie base via Testcontainers.
- Couverture cible > 80 % sur `domain` et `application`.

## 7. Anti-patterns à refuser en revue
- Logique métier dans les routes FastAPI ou les modèles ORM.
- Arguments par défaut mutables (`def f(x=[])`).
- `except Exception: pass`.
- Mélange sync/async (appel `requests` dans une route async).
- Notebooks utilisés comme code de production.

## 8. Commandes
```bash
uv sync
uv run ruff check . && uv run ruff format --check .
uv run mypy src
uv run pytest --cov=src
uv run pip-audit
```
