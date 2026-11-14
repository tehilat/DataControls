from __future__ import annotations

import copy

import pytest
from fastapi.testclient import TestClient

from main import app
from schemas import Catalog
from tests.catalog_factory import build_catalog

_CATALOG = build_catalog()


@pytest.fixture
def catalog_json() -> dict:
    return copy.deepcopy(_CATALOG)


@pytest.fixture
def catalog(catalog_json) -> Catalog:
    return Catalog.model_validate(catalog_json)


@pytest.fixture
def client() -> TestClient:
    return TestClient(app)
