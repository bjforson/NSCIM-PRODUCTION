"""Reset unreviewed failed jobs to pending for reprocessing."""
import psycopg2, os

pw = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', dbname='nickscan_production', user='postgres', password=pw)
cur = conn.cursor()

cur.execute("DELETE FROM image_split_assignments WHERE job_id IN (SELECT id FROM image_split_jobs WHERE analyst_verdict IS NULL AND status='failed')")
print(f'Deleted {cur.rowcount} assignments')

cur.execute("DELETE FROM image_split_results WHERE job_id IN (SELECT id FROM image_split_jobs WHERE analyst_verdict IS NULL AND status='failed')")
print(f'Deleted {cur.rowcount} results')

cur.execute("UPDATE image_split_jobs SET status='pending', best_strategy=NULL, best_score=NULL, split_x=NULL, completed_at=NULL, error_message=NULL WHERE analyst_verdict IS NULL AND status='failed'")
print(f'Reset {cur.rowcount} jobs to pending')

conn.commit()
conn.close()
print('Done.')
