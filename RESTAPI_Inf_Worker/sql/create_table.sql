-- Run this once if you want to create the table manually.
CREATE TABLE IF NOT EXISTS api_responses (
  id BIGSERIAL PRIMARY KEY,
  response jsonb,
  status_code integer,
  received_at timestamptz DEFAULT now()
);
