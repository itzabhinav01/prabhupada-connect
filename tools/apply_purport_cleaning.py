import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')

db_path = "Database/prabhupada_corpus.db"
conn = sqlite3.connect(db_path)
c = conn.cursor()

# Check tables
c.execute("SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view')")
tables = c.fetchall()
print("Tables:", [t[0] for t in tables])

def clean_record_purport(rk, bk, ref, p):
    original = p
    modified = p

    # Fix inline Folio formatting artifact l0 at beginning of lines
    modified = re.sub(r'(?m)^l0—', '— ', modified)
    modified = re.sub(r'(?m)^l0([A-Za-z])', r'\1', modified)
    
    # 1. BG (Bhagavad-gita)
    if bk == 'BG':
        m = re.search(r'(Thus ends? the Bhaktivedanta Purports? to the [^\n\.]+(?:\n[^\n\.]+)?\.)', modified, re.IGNORECASE)
        if m:
            end_pos = m.end()
            if end_pos < len(modified.strip()):
                modified = modified[:end_pos]
                
    # 2. CC (Caitanya-caritamrta)
    elif bk in ('ĀDI', 'MADHYA', 'ANTYA') or 'CC' in bk:
        modified = re.sub(r'\s*\n+\s*\\?\*(?:Adi|Madhya|Antya)\s+\d+:.*$', '', modified, flags=re.DOTALL | re.IGNORECASE)
        
    # 3. SB (Srimad-Bhagavatam)
    elif bk.startswith('SB'):
        modified = re.sub(r'\s*\n+\s*\\?\*SB\s+\d+\.\d+:.*$', '', modified, flags=re.DOTALL | re.IGNORECASE)
        modified = re.sub(r'\s*\n+\s*l0Canto\s+\d+.*$', '', modified, flags=re.DOTALL | re.IGNORECASE)
        
    # 4. NOD-51
    if rk == 'NOD-51':
        modified = re.sub(r'\s*\n+\s*\\?\*Easy Journey to Other Planets.*$', '', modified, flags=re.DOTALL)
        
    # 5. LOB-48
    if rk == 'LOB-48':
        modified = re.sub(r'\s*\n+\s*\\?\*Journey of Self-Discovery.*$', '', modified, flags=re.DOTALL)
        
    # Generic cleanup of dangling folio header tags at the end of any purport
    modified = re.sub(r'\s*\n+\s*l0\\?\*.*$', '', modified)
    modified = re.sub(r'\s*\n+\s*VB\d+.*$', '', modified)

    return modified.strip(), modified.strip() != original.strip()

c.execute("SELECT RecordKey, BookKey, Reference, Purports FROM Records WHERE Purports IS NOT NULL")
rows = c.fetchall()

updates = []
for rk, bk, ref, p in rows:
    cleaned, changed = clean_record_purport(rk, bk, ref, p)
    if changed:
        updates.append((cleaned, rk))

print(f"Applying updates to {len(updates)} records...")
c.executemany("UPDATE Records SET Purports = ? WHERE RecordKey = ?", updates)

# Check if RecordsFts or similar exists
has_fts = any('fts' in t[0].lower() for t in tables)
print("FTS table present:", has_fts)
if has_fts:
    fts_table = [t[0] for t in tables if 'fts' in t[0].lower()][0]
    print(f"Syncing FTS table {fts_table}...")
    try:
        # Check FTS columns
        c.execute(f"PRAGMA table_info({fts_table})")
        fts_cols = [ci[1] for ci in c.fetchall()]
        print("FTS cols:", fts_cols)
        # Update or rebuild FTS
        # In SQLite FTS5, we can run 'rebuild' command: INSERT INTO RecordsFts(RecordsFts) VALUES('rebuild');
        c.execute(f"INSERT INTO {fts_table}({fts_table}) VALUES('rebuild')")
        print("FTS rebuild succeeded.")
    except Exception as e:
        print("FTS rebuild notice:", e)

conn.commit()
print("Database committed successfully.")

# Verify BG-3-43, BG-18-78, NOD-51, LOB-48
c.execute("SELECT RecordKey, Reference, Purports FROM Records WHERE RecordKey IN ('BG-3-43', 'BG-18-78', 'NOD-51', 'LOB-48')")
for rk, ref, p in c.fetchall():
    print(f"\n[VERIFIED {rk}] ({ref}):")
    print("TAIL:", repr(p[-150:]))

conn.close()
