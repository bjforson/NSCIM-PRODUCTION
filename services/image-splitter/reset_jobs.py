"""Reset all unreviewed jobs to pending so they reprocess with the new strategy."""
import psycopg2, os

pw = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', dbname='nickscan_production', user='postgres', password=pw)
cur = conn.cursor()

cur.execute("SELECT status, COUNT(*) FROM image_split_jobs WHERE analyst_verdict IS NULL GROUP BY status")
print('Unreviewed jobs before reset:')
for row in cur.fetchall():
    print(f'  {row[0]}: {row[1]}')

cur.execute("DELETE FROM image_split_assignments WHERE job_id IN (SELECT id FROM image_split_jobs WHERE analyst_verdict IS NULL AND status IN ('completed','failed'))")
print(f'Deleted {cur.rowcount} old assignments (deleted first due to FK)')

cur.execute("DELETE FROM image_split_results WHERE job_id IN (SELECT id FROM image_split_jobs WHERE analyst_verdict IS NULL AND status IN ('completed','failed'))")
print(f'Deleted {cur.rowcount} old strategy results')
print(f'Deleted {cur.rowcount} old assignments')

cur.execute("UPDATE image_split_jobs SET status='pending', best_strategy=NULL, best_score=NULL, split_x=NULL, completed_at=NULL, error_message=NULL WHERE analyst_verdict IS NULL AND status IN ('completed','failed')")
print(f'Reset {cur.rowcount} jobs to pending')

conn.commit()
conn.close()
print('Done.')
