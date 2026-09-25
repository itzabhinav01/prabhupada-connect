import sqlite3
import re
import sys
import os

sys.stdout.reconfigure(encoding='utf-8')

DB_PATH = r'C:\VedaBaseModern2\Database\prabhupada_corpus.db'

def heal_corpus():
    conn = sqlite3.connect(DB_PATH)
    cur = conn.cursor()
    
    print("--- 1. HEALING TLC-32 ---")
    cur.execute("SELECT rowid, Purports FROM Records WHERE RecordKey='TLC-32'")
    row = cur.fetchone()
    if row:
        rowid, text = row
        cutoff = "enter into the transcendental association of Rādhā and Kṛṣṇa."
        idx = text.find(cutoff)
        if idx != -1:
            clean_text = text[:idx + len(cutoff)].strip()
            cur.execute("UPDATE Records SET Purports=? WHERE RecordKey='TLC-32'", (clean_text,))
            print(f"TLC-32 trimmed from {len(text)} bytes to {len(clean_text)} bytes.")
            # Update FTS
            cur.execute("DELETE FROM RecordsFts WHERE rowid=?", (rowid,))
            cur.execute("""
                INSERT INTO RecordsFts (rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
                SELECT rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports
                FROM Records WHERE rowid=?
            """, (rowid,))
    
    print("\n--- 2. HEALING BG-4-5 AND BG-13-33 ---")
    # BG-4-5
    cur.execute("SELECT rowid, Purports FROM Records WHERE RecordKey='BG-4-5'")
    row = cur.fetchone()
    if row:
        rowid, purp = row
        cur.execute("SELECT Devanagari FROM Records WHERE RecordKey='BG-4-6'")
        bg46_dev = cur.fetchone()[0]
        # Replace broken Indevr quote
        broken_bg4 = re.compile(r'AJaae_iPa SaṁVYaYaaTMaa >aUTaaNaaMaqṅrae_iPa SaNa\(\s*\)\s*\n+\s*Pa\[k\*-iTa& SvaMaiDañaYa SaM>avaMYaaTMaMaaYaYaa \)\)\s*6\s*\)\)')
        purp_new = broken_bg4.sub(bg46_dev.strip(), purp)
        if purp_new != purp:
            cur.execute("UPDATE Records SET Purports=? WHERE RecordKey='BG-4-5'", (purp_new,))
            cur.execute("DELETE FROM RecordsFts WHERE rowid=?", (rowid,))
            cur.execute("""
                INSERT INTO RecordsFts (rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
                SELECT rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports
                FROM Records WHERE rowid=?
            """, (rowid,))
            print("BG-4-5 purport healed with authentic Devanagari.")

    # BG-13-33
    cur.execute("SELECT rowid, Purports FROM Records WHERE RecordKey='BG-13-33'")
    row = cur.fetchone()
    if row:
        rowid, purp = row
        cur.execute("SELECT Devanagari FROM Records WHERE RecordKey='BG-13-34'")
        bg1334_dev = cur.fetchone()[0]
        broken_bg13 = re.compile(r'YaQaa Pa\[k-aXaYaTYaek-\"\s*k\*-Tḍ&\s*l/aek-iMaMa&\s*riv\"\s*\)\s*\n+\s*\+ae\\a&\s*\+ae\\aq\s*TaQaa\s*k\*-Tḍ&\s*Pa\[k-aXaYaiTa\s*>aarTa\s*\)\)\s*34\s*\)\)')
        purp_new = broken_bg13.sub(bg1334_dev.strip(), purp)
        if purp_new != purp:
            cur.execute("UPDATE Records SET Purports=? WHERE RecordKey='BG-13-33'", (purp_new,))
            cur.execute("DELETE FROM RecordsFts WHERE rowid=?", (rowid,))
            cur.execute("""
                INSERT INTO RecordsFts (rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
                SELECT rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports
                FROM Records WHERE rowid=?
            """, (rowid,))
            print("BG-13-33 purport healed with authentic Devanagari.")

    print("\n--- 3. HEALING TQK (TEACHINGS OF QUEEN KUNTI 1-26) ---")
    for i in range(1, 27):
        rk = f"TQK-{i}"
        sb_rk = f"SB-1.8-{17+i}"
        
        cur.execute("SELECT rowid, Purports FROM Records WHERE RecordKey=?", (rk,))
        tqk_row = cur.fetchone()
        if not tqk_row: continue
        rowid, purp = tqk_row
        
        # Get authentic verse fields from SB
        cur.execute("SELECT Devanagari, Transliteration, Synonyms, Translation FROM Records WHERE RecordKey=?", (sb_rk,))
        sb_verse = cur.fetchone()
        if not sb_verse: continue
        sb_dev, sb_translit, sb_syn, sb_translation = sb_verse
        
        # Extract pure purport starting from PURPORT
        idx_purp = purp.find("PURPORT")
        if idx_purp != -1:
            clean_purp = purp[idx_purp + len("PURPORT"):].strip()
        else:
            # Fallback if no "PURPORT" header
            idx_trans = purp.find("TRANSLATION")
            if idx_trans != -1:
                after_trans = purp[idx_trans:]
                m = re.search(r'TRANSLATION\s*\n+(.*?)\n\n+(?:[—\-\[\(].*?Bhāgavatam.*?\n+)?(.*)$', after_trans, re.DOTALL)
                clean_purp = m.group(2).strip() if m else purp
            else:
                clean_purp = purp
        
        cur.execute("""
            UPDATE Records 
            SET Devanagari=?, Transliteration=?, Synonyms=?, Translation=?, Purports=?
            WHERE RecordKey=?
        """, (sb_dev, sb_translit, sb_syn, sb_translation, clean_purp, rk))
        
        # Update FTS
        cur.execute("DELETE FROM RecordsFts WHERE rowid=?", (rowid,))
        cur.execute("""
            INSERT INTO RecordsFts (rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
            SELECT rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports
            FROM Records WHERE rowid=?
        """, (rowid,))
        print(f"Healed {rk} with authentic {sb_rk} Devanagari and clean Purport.")

    print("\n--- 4. HEALING TLK (TEACHINGS OF LORD KAPILA 1-18) ---")
    tlk_map = {
        1: "SB-3.25-1",
        2: "SB-3.25-2",
        3: "SB-3.25-3",
        4: "SB-3.25-4",
        5: "SB-3.25-5",
        6: "SB-3.25-7",
        7: "SB-3.25-12",
        8: "SB-3.25-14",
        9: "SB-3.25-15",
        10: "SB-3.25-18",
        11: "SB-3.25-21",
        12: "SB-3.25-25",
        13: "SB-3.25-28",
        14: "SB-3.25-31",
        15: "SB-3.25-34",
        16: "SB-3.25-37",
        17: "SB-3.25-41",
        18: "SB-3.25-43"
    }

    for ch_num, sb_rk in tlk_map.items():
        rk = f"TLK-{ch_num}"
        cur.execute("SELECT rowid, Purports FROM Records WHERE RecordKey=?", (rk,))
        tlk_row = cur.fetchone()
        if not tlk_row: continue
        rowid, purp = tlk_row

        cur.execute("SELECT Devanagari, Transliteration, Synonyms, Translation FROM Records WHERE RecordKey=?", (sb_rk,))
        sb_verse = cur.fetchone()
        if not sb_verse: continue
        sb_dev, sb_translit, sb_syn, sb_translation = sb_verse

        idx_purp = purp.find("PURPORT")
        if idx_purp != -1:
            clean_purp = purp[idx_purp + len("PURPORT"):].strip()
        else:
            idx_trans = purp.find("TRANSLATION")
            if idx_trans != -1:
                after_trans = purp[idx_trans:]
                m = re.search(r'TRANSLATION\s*\n+(.*?)\n\n+(?:[—\-\[\(].*?Bhāgavatam.*?\n+)?(.*)$', after_trans, re.DOTALL)
                clean_purp = m.group(2).strip() if m else purp
            else:
                clean_purp = purp

        cur.execute("""
            UPDATE Records 
            SET Devanagari=?, Transliteration=?, Synonyms=?, Translation=?, Purports=?
            WHERE RecordKey=?
        """, (sb_dev, sb_translit, sb_syn, sb_translation, clean_purp, rk))

        cur.execute("DELETE FROM RecordsFts WHERE rowid=?", (rowid,))
        cur.execute("""
            INSERT INTO RecordsFts (rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
            SELECT rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports
            FROM Records WHERE rowid=?
        """, (rowid,))
        print(f"Healed {rk} with authentic {sb_rk} Devanagari and clean Purport.")

    print("\n--- 5. ADDING FTS UPDATE / DELETE TRIGGERS ---")
    cur.execute("""
        CREATE TRIGGER IF NOT EXISTS trg_records_fts_update AFTER UPDATE ON Records
        BEGIN
            DELETE FROM RecordsFts WHERE rowid = old.rowid;
            INSERT INTO RecordsFts(rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
            VALUES (new.rowid, new.RecordKey, new.Reference, new.Title, new.Devanagari, new.Transliteration, new.Synonyms, new.Translation, new.Purports);
        END;
    """)
    cur.execute("""
        CREATE TRIGGER IF NOT EXISTS trg_records_fts_delete AFTER DELETE ON Records
        BEGIN
            DELETE FROM RecordsFts WHERE rowid = old.rowid;
        END;
    """)

    conn.commit()
    print("Database changes committed successfully!")

    print("\n--- 6. RUNNING VACUUM TO RECLAIM SPACE ---")
    cur.execute("VACUUM;")
    conn.commit()
    conn.close()
    print("VACUUM completed!")

if __name__ == '__main__':
    heal_corpus()
