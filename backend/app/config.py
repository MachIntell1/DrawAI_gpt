from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    app_name: str = "MachIntell Drawing Planner"
    app_env: str = "development"
    log_level: str = "INFO"
    max_request_bytes: int = 8 * 1024 * 1024
    plan_ttl_seconds: int = 86_400
    allow_v1_compatibility: bool = True
    api_key: str | None = None

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")


@lru_cache
def get_settings() -> Settings:
    return Settings()
