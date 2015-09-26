﻿/* 
 * Copyright (C) 2014-2015 John Torjo
 *
 * This file is part of LogWizard
 *
 * LogWizard is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * LogWizard is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *
 * If you wish to use this code in a closed source application, please contact john.code@torjo.com
*/
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net.Configuration;
using System.Text;
using System.Windows.Forms;

namespace lw_common.ui {
    // Show by time - not implemented yet
    public partial class note_ctrl : UserControl {

        private string author_name_ = "", author_initials_ = "";
        private Color author_color_ = util.transparent;

        public class note {
            public string author_name = "";
            public string author_initials = "";
            public Color author_color = util.transparent;

            public string note_text = "";

            // NOT persisted: if this were persisted, all notes would end up as being added by current user
            //
            // if true, we always use the existing author name/initials/color
            // the reason we have this: if this was made by current user, always use the most up-to-date name/initials/color
            public bool made_by_current_user = false;

            public bool deleted = false;

            // NOT persisted
            //
            // if true, it's a note added in this session 
            // IMPORTANT: the notes added in this session are always added at the end (if they are new notes, to new lines)
            public bool is_new = false;
        }

        public class line {
            protected bool Equals(line other) {
                return idx == other.idx && string.Equals(view_name, other.view_name) && string.Equals(msg, other.msg);
            }

            public override bool Equals(object obj) {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((line) obj);
            }

            public override int GetHashCode() {
                unchecked {
                    var hashCode = idx;
                    hashCode = (hashCode * 397) ^ (view_name != null ? view_name.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (msg != null ? msg.GetHashCode() : 0);
                    return hashCode;
                }
            }

            public static bool operator ==(line left, line right) {
                return Equals(left, right);
            }

            public static bool operator !=(line left, line right) {
                return !Equals(left, right);
            }

            public void copy_from(line other) {
                idx = other.idx;
                view_name = other.view_name;
                msg = other.msg;
            }

            public void clear() {
                idx = -1;
                view_name = msg = "";
            }

            public int idx = -1;
            public string view_name = "";
            public string msg = "";
        }

        private class note_item {
            public readonly note_ctrl self = null;

            // if null, it's the header
            public note the_note = null;

            // each note has a unique ID -this can be used when adding a note that is in reply to someone
            public readonly int note_id;

            // in case this is a note that is in reply to another note, this contains the ID of the other note we're replying to
            public readonly int reply_id;

            // pointer to a line - each line has a unique ID (different from its index)
            private int line_id_;

            // when was this note added (could be useful when sorting by time)
            public readonly DateTime utc_added = DateTime.UtcNow;

            // if true, this is the header (information about the line itself)
            public bool is_note_header {
                get { return the_note == null; }
            }

            public bool is_cur_line {
                get { return line_id_ == self.cur_line_id_; }
            }

            public int line_id {
                get { return line_id_; }
                set {
                    // you can only reset the line ID - if it is the current line
                    Debug.Assert(line_id_ == self.cur_line_id_);
                    line_id_ = value;
                }
            }

            /////////////////////////////////////////////////////////////////////////////////////////
            // Constructor

            public note_item(note_ctrl self, note n, int unique_id, int reply_id, int line_id) {
                Debug.Assert( self.lines_.ContainsKey(line_id));

                the_note = n;
                this.self = self;
                note_id = unique_id;
                line_id_ = line_id;
                this.reply_id = reply_id;
            }

            public note_item(note_ctrl self, int line_id) {
                Debug.Assert( self.lines_.ContainsKey(line_id));

                this.self = self;
                line_id_ = line_id;
                the_note = null;
            }
            /////////////////////////////////////////////////////////////////////////////////////////
            // remaining - getters

            public string the_idx {
                get {
                    if (is_note_header)
                        return "";
                    int idx = 0;

                    for (int other_idx = 0; other_idx < self.notesCtrl.GetItemCount(); ++other_idx) {
                        var i = self.notesCtrl.GetItem(other_idx).RowObject as note_item;
                        if (i == this)
                            break;

                        if (!i.is_note_header)
                            ++idx;
                    }

                    return "" + (idx + 1);
                }
            }

            public string the_line {
                get {
                    return the_note != null ? the_note.author_initials : "" + (self.lines_[line_id].idx + 1);
                }
            }


            public int indent {
                get {
                    int ind = 0;
                    note_item cur = this;
                    while (cur.reply_id > 0) {
                        ++ind;
                        cur = self.notes_sorted_by_line_id_.FirstOrDefault(x => x.note_id == reply_id);
                        if (cur == null)
                            break;
                    }
                    return ind;
                }
            }


            public string the_text {
                get {
                    if (the_note != null) {
                        // gets the note that is printed on screen - on the list
                        // ignore #... at beginning of msg
                        return the_note.note_text;
                    } else
                        return self.lines_[line_id].msg;
                }
            }

            public Color text_fg {
                get {
                    // look into # at beginning of msg
                    return Color.Blue;                     
                }
            }
            public Color line_fg {
                get {
                    return Color.Blue;                     
                }
            }
        }

        private Dictionary<int, line> lines_ = new Dictionary<int,line>();

        // the are kept as if they were sorted by line, then time - first the line (header), then all the notes related to that line
        private List<note_item> notes_sorted_by_line_id_ = new List<note_item>();

        private int next_note_id_ = 0, next_line_id_ = 0;

        // special ID for the current line
        private int cur_line_id_ = int.MaxValue - 1;

        private string file_name_ = "";

        private int ignore_change_ = 0;

        private line cur_line {
            get { return lines_[cur_line_id_]; }
        }

        private bool dirty_ = false;

        public note_ctrl() {
            InitializeComponent();
            lines_.Add(cur_line_id_, new line());
            update_cur_note_controls();
        }

        private void note_ctrl_Load(object sender, EventArgs e) {
        }

        // the controls we can navigate through with TAB hotkey
        public List<Control> tab_navigatable_controls {
            get {
                var nav = new List<Control>();
                nav.Add(notesCtrl);
                if ( curNote.Enabled)
                    nav.Add(curNote);
                return nav;
            }
        }

        public void set_author(string author_name, string author_initials, Color notes_color) {
            author_name_ = author_name;
            author_initials_ = author_initials;
            author_color_ = notes_color;
            // update anything that was added by me before the changes were made
            foreach (note_item n in notes_sorted_by_line_id_)
                if (n.the_note != null && n.the_note.made_by_current_user) {
                    n.the_note.author_name = author_name;
                    n.the_note.author_initials = author_initials;
                    n.the_note.author_color = notes_color;
                }
            notesCtrl.Refresh();
        }

        // helper - add to the currently selected line
        public int add_note(note n, int note_id, int reply_to_note_id) {
            Debug.Assert(cur_line != null);
            if (cur_line == null)
                return -1;

            line new_ = new line();
            new_.copy_from(cur_line);
            return add_note(new_, n, note_id, reply_to_note_id);
        }

        // returns the ID assigned to this note that we're adding;
        // if -1, something went wrong
        //
        // note_id - the unique ID of the note, or -1 if a new unique ID is to be created
        //           the reason we have this is that we can persist notes (together with their IDs)
        public int add_note(line l, note n, int note_id, int reply_to_note_id) {
            // a note: can be added on a line, or as reply to someone
            // new notes: always at the end

            int line_id = -1;
            var found_line = lines_.FirstOrDefault(x => x.Value == l);
            Debug.Assert(found_line.Value != null);
            if (cur_line == l) {
                // it's the current line
                line_id = ++next_line_id_;
                lines_.Add(line_id, l);
            } 
            else 
                line_id = found_line.Key;

            note_id = note_id >= 0 ? note_id : ++next_note_id_;
            // ... this note should not exist already
            Debug.Assert( notes_sorted_by_line_id_.Count(x => x.note_id == note_id) == 0);
            if ( reply_to_note_id >= 0)
                // note we're replying to should exist already
                Debug.Assert( notes_sorted_by_line_id_.Count(x => x.note_id == reply_to_note_id) == 1);

            var new_ = new note_item(this, n, note_id, reply_to_note_id, line_id);
            if (next_note_id_ < note_id)
                next_note_id_ = note_id;

            bool inserted = false;
            if (reply_to_note_id > 0) {
                // add this note as the last reply to this reply note
                var note = notes_sorted_by_line_id_.FirstOrDefault(x => x.line_id == line_id);
                // when it's a reply, we need to find the original note!
                Debug.Assert(note != null);
                if (note != null) {
                    Debug.Assert(note.is_note_header);
                    // everything following this, is related to this line (until we get to next line)
                    int idx_note = notes_sorted_by_line_id_.IndexOf(note);
                    for (; idx_note < notes_sorted_by_line_id_.Count && !inserted; idx_note++)
                        if (notes_sorted_by_line_id_[idx_note].line_id == line_id) {
                            if (reply_to_note_id > 0) {
                                // in this case, look for this note (that we're replying to)
                                if (notes_sorted_by_line_id_[idx_note].note_id == reply_to_note_id) {
                                    notes_sorted_by_line_id_.Insert(idx_note + 1, new_);
                                    inserted = true;
                                }
                            } else {
                                // look for the last note about this line, and insert it after that
                                if ( idx_note < notes_sorted_by_line_id_.Count - 1)
                                    if (notes_sorted_by_line_id_[idx_note + 1].line_id != line_id) {
                                        // found the last note that relates to this line
                                        notes_sorted_by_line_id_.Insert(idx_note + 1, new_);
                                        inserted = true;                                        
                                    }
                            }
                        } else
                            // went to next line
                            break;
                }
            }
            else {
                var note = notes_sorted_by_line_id_.FirstOrDefault(x => x.line_id == line_id);
                if (note != null) {
                    // in this case, there may be other notes - we're adding it to the end (as last note on this line)
                    Debug.Assert(note.is_note_header);
                    int idx = notes_sorted_by_line_id_.IndexOf(note);
                    for ( ; idx < notes_sorted_by_line_id_.Count; ++idx)
                        if ( idx < notes_sorted_by_line_id_.Count - 1)
                            if (notes_sorted_by_line_id_[idx + 1].line_id != line_id)
                                break;
                    if ( idx < notes_sorted_by_line_id_.Count)
                        notes_sorted_by_line_id_.Insert(idx, new_);
                    else 
                        notes_sorted_by_line_id_.Add(new_);
                    inserted = true;
                } else {
                    // this is the first entry that relates to this line
                    // ... find the note beore which we should insert ourselves
                    note = notes_sorted_by_line_id_.FirstOrDefault(x => x.line_id > line_id);
                    if (note != null) {
                        int idx = notes_sorted_by_line_id_.IndexOf(note);
                        notes_sorted_by_line_id_.Insert(idx, new_);
                    } else {
                        notes_sorted_by_line_id_.Add(new_);
                    }
                    inserted = true;
                }
            }
            Debug.Assert(inserted);

            // update the UI
            if (inserted)
                add_note_to_ui(new_);

            dirty_ = true;
            return new_.note_id;
        }

        private void add_note_to_ui(note_item last_note) {
            bool add_to_ui = !last_note.the_note.deleted || (last_note.the_note.deleted && showDeletedLines.Checked);
            if (!add_to_ui)
                return;

            int insert_idx = -1;
            if (last_note.reply_id >= 0) {
                // it's a note on reply to another note
                for (int note_idx = 0; note_idx < notesCtrl.GetItemCount() && insert_idx < 0; ++note_idx) {
                    var i = notesCtrl.GetItem(note_idx).RowObject as note_item;
                    if (i.note_id == last_note.reply_id) 
                        insert_idx = note_idx + 1;
                }
            } else {
                // new note
                for (int note_idx = 0; note_idx < notesCtrl.GetItemCount(); ++note_idx) {
                    var i = notesCtrl.GetItem(note_idx).RowObject as note_item;
                    int line_id = i.line_id != cur_line_id_ ? i.line_id : find_cur_line_id();
                    
                    if (line_id == last_note.line_id) {
                        insert_idx = note_idx + 1;
                        bool insert_at_the_end = i.line_id == cur_line_id_;
                        if (insert_at_the_end) {
                            // we need to update the line, so that it does not point to current-line anymore
                            i.line_id = line_id;
                        }
                    }
                }
            }
            Debug.Assert( insert_idx >= 0);
            if (insert_idx >= 0) {
                ++ignore_change_;
                notesCtrl.InsertObjects(insert_idx, new object[] {last_note});
                // now, select it as well
                notesCtrl.SelectObject(last_note);
                --ignore_change_;
            }
        }

        private int find_cur_line_id() {
            // the last line - is always the last
            Debug.Assert(lines_.Count >= 2);
            foreach ( var l in lines_)
                if (l.Key != cur_line_id_ && l.Value == cur_line)
                    return l.Key;
            // we could not find it!
            Debug.Assert(false);
            return cur_line_id_;
        }


        public void del_note(int note_id) {
            // note: delete someone else's note -> it's grayed (hidden)
            //       delete your note: it's fully deleted + copied to clipboard -> ALL THE notes you delete - keep them internally and the user should be able to access them
            //       (just in case he did a mistake)

            // if i delete a note from line ,and no other notes on that line:
            // if it was from current user, delete the line note as well ; otherwise, just hide it
            dirty_ = true;
        }

        // this sets the line the user is viewing - by default, this is what the user is commenting on
        public void set_current_line(line l) {
            cur_line.copy_from(l);

            /* Possibilities:
            
            - case 1 - there are already notes on this line

                1a - there are already notes on this line, and the selection is already one of them
                1b - there are already notes on this line, and the selection is on another note (or nowhere)

            - case 2 - there are no notes on this line
            */
            note_item header = null;
            for (int idx = 0; idx < notesCtrl.GetItemCount() && header == null ; ++idx) {
                var i = notesCtrl.GetItem(idx).RowObject as note_item;
                if (i.is_note_header && lines_[i.line_id].idx == l.idx && !i.is_cur_line)
                    header = i;
            }

            bool last_note_is_cur_line = notesCtrl.GetItemCount() > 0 && (notesCtrl.GetItem(notesCtrl.GetItemCount() - 1).RowObject as note_item).is_cur_line;
            if (header != null) {
                // already a note on this line, not needed now
                if (last_note_is_cur_line)
                    notesCtrl.RemoveObject(notesCtrl.GetItem(notesCtrl.GetItemCount() - 1).RowObject);

                int sel = notesCtrl.SelectedIndex;
                var sel_item = sel >= 0 ? notesCtrl.GetItem(sel).RowObject as note_item : null;
                if (sel_item != null && sel_item.line_id == header.line_id)
                    // 1a - on a note from this line
                    return;

                // 1b - on a note from another note
                // I will select this note
                notesCtrl.SelectObject(header);
            } else {
                // 2 - no notes on this line
                if ( !last_note_is_cur_line) 
                    notesCtrl.AddObject( new note_item(this, cur_line_id_));
                var last = notesCtrl.GetItem(notesCtrl.GetItemCount() - 1).RowObject as note_item;
                refresh_note(last);

                if (notesCtrl.SelectedIndex < 0)
                    // select the "last line"
                    notesCtrl.SelectedIndex = notesCtrl.GetItemCount() - 1;
            }

            update_cur_note_controls();
        }






        // notifies the views of what the user has selected (what line / view)
        public void sync_to_views() {
            // FIXME            
        }

        // gets notified when the view and/or line has changed
        public void sync_from_views() {
            // FIXME            
        }

        public void undo() {
            // FIXME

            // Ctrl-Z -> write a msg "All your deleted notes have been copied to clipboard - first your own, then the ones from the other authors"
        }

        public void load(string file_name) {
            ++ignore_change_;
            if (file_name_ != "")
                // in this case, we're loading the notes from somewhere else, save existing first
                save();

            file_name_ = file_name;

            notes_sorted_by_line_id_.Clear();
            notesCtrl.ClearObjects();
            cur_line.clear();

            // if author name = ourselves -> made_by_current_user = true
        
            // update last_note_id and last_line_id
            dirty_ = false;
            --ignore_change_;
        }

        public void save() {
            // needs to be loaded first
            if (file_name_ == "")
                return;
            if (!dirty_)
                return;

            // don't save the cur_line

            dirty_ = false;
        }

        private void refresh_note(note_item i) {
            notesCtrl.RefreshObject(i);
            // FIXME fg + bg - see about whether is deleted or not
        }

        private void curNote_KeyDown(object sender, KeyEventArgs e) {
            string s = util.key_to_action(e);
            if (s == "return") {
                // add note
                string note_text = curNote.Text.Trim();
                if (note_text == "")
                    return;
                // see if I'm on a user's note (thus, i'm updating), or on a line (thus, i'm adding a note for that line)
                int reply_to_note_id = -1;
                int sel = notesCtrl.SelectedIndex;
                if (sel >= 0) {
                    var i = notesCtrl.GetItem(sel).RowObject as note_item;
                    bool is_edit = i.the_note != null && i.the_note.made_by_current_user;
                    if (is_edit) {
                        dirty_ = true;
                        i.the_note.note_text = note_text;
                        refresh_note(i);
                        return;
                    }
                    bool is_reply = i.the_note != null && !i.the_note.made_by_current_user;
                    if (is_reply)
                        reply_to_note_id = i.note_id;
                }

                note new_ = new note() { author_name = author_name_, author_initials = author_initials_, 
                    author_color = author_color_, note_text = note_text, made_by_current_user = true, is_new = true };
                add_note(new_, -1, reply_to_note_id);
            }
        }

        private void curNote_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e) {
            string s = util.key_to_action(e);
            if (s == "return")
                e.IsInputKey = true;
        }
        private void curNote_KeyUp(object sender, KeyEventArgs e) {
            string s = util.key_to_action(e);
            if (s == "return") 
                notesCtrl.Focus();
        }

        private void notesCtrl_SelectedIndexChanged(object sender, EventArgs e) {
            if (ignore_change_ > 0)
                return;

            update_cur_note_controls();
        }

        private void update_cur_note_controls() {
            int sel = notesCtrl.SelectedIndex;

            // note: the only time when sel < 0 is possible only when the user hasn't selected any line yet
            //       (thus, even cur_line is pointing nowhere)
            curNote.Enabled = sel >= 0;
            addNoteToLine.Visible = sel >= 0;
            addNoteToLineLabel.Visible = sel >= 0;

            addNoteToLine.Text = "";
            if (sel >= 0) {
                var i = notesCtrl.GetItem(sel).RowObject as note_item;
                if ( i.the_note != null)
                    addNoteToLineLabel.Text = i.the_note.made_by_current_user ? "Edit Note to Line" : "Reply to Note from " + i.the_note.author_initials;
                else
                    addNoteToLineLabel.Text = "Add Note to Line";
                addNoteToLine.Text = "[" + (lines_[i.line_id].idx + 1) + "]";
            }
        }

        private void saveTimer_Tick(object sender, EventArgs e) {
            if (dirty_)
                save();
        }

        private void notesCtrl_Enter(object sender, EventArgs e) {
            curNote.BackColor = Color.AliceBlue;
        }

        private void notesCtrl_Leave(object sender, EventArgs e) {
            curNote.BackColor = Color.White;
        }


    }
}
