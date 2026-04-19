-- NickComms Gateway Database Initialization
-- Run this ONCE to create the database before starting the service.
-- The service will auto-migrate tables via EF Core on startup.

CREATE DATABASE nick_comms
    WITH OWNER = postgres
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.UTF-8'
    LC_CTYPE = 'en_US.UTF-8'
    TEMPLATE = template0;

-- Grant access
GRANT ALL PRIVILEGES ON DATABASE nick_comms TO postgres;
