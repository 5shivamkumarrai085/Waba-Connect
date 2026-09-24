import React, { useEffect, useRef, useState } from 'react'
import {
  Bold, Italic, Underline, List, ListOrdered, Link2, Image as ImageIcon, Code2, RemoveFormatting
} from 'lucide-react'
import './RichTextEditor.css'

interface RichTextEditorProps {
  value: string
  onChange: (html: string) => void
  placeholder?: string
  /** Rendered under the toolbar — the variable chips, in the template editor. */
  toolbarExtra?: React.ReactNode
  id?: string
}

type Block = 'p' | 'h1' | 'h2' | 'h3'

/**
 * A small formatting editor for email HTML.
 *
 * Two modes, and both are necessary. The visual mode is what an operator writing copy wants; the
 * source mode is what anyone pasting a designer's email HTML needs, because a contentEditable
 * will quietly rewrite markup it does not understand — and email HTML is full of exactly the
 * table layouts and inline styles it does not understand.
 *
 * Uses document.execCommand, which is deprecated but remains the only thing every browser
 * implements for contentEditable formatting. The alternative is a full editor framework, which is
 * a large dependency for a handful of formatting buttons. The server sanitizes whatever comes out
 * regardless, so a browser quirk cannot turn into an injection.
 */
export const RichTextEditor: React.FC<RichTextEditorProps> = ({
  value,
  onChange,
  placeholder,
  toolbarExtra,
  id
}) => {
  const editorRef = useRef<HTMLDivElement>(null)
  const [isSourceMode, setIsSourceMode] = useState(false)

  // Only pushed into the DOM when it differs from what the editor already holds. Writing on every
  // render would reset the caret to the start on each keystroke.
  useEffect(() => {
    if (isSourceMode) return
    const editor = editorRef.current
    if (editor && editor.innerHTML !== value) editor.innerHTML = value || ''
  }, [value, isSourceMode])

  const exec = (command: string, argument?: string) => {
    // The editor must hold focus or the command applies to whatever the browser last had
    // selected — often something outside this component entirely.
    editorRef.current?.focus()
    document.execCommand(command, false, argument)
    if (editorRef.current) onChange(editorRef.current.innerHTML)
  }

  const applyBlock = (block: Block) => exec('formatBlock', block === 'p' ? '<p>' : `<${block}>`)

  const insertLink = () => {
    const url = window.prompt('Link URL', 'https://')
    if (!url) return

    // Only http(s) and mailto. The server strips javascript: anyway, but letting it into the
    // editor means an operator sees a link that silently disappears on save.
    if (!/^(https?:|mailto:)/i.test(url)) {
      window.alert('Only http, https and mailto links are allowed.')
      return
    }

    exec('createLink', url)
  }

  const insertImage = () => {
    const url = window.prompt('Image URL', 'https://')
    if (!url) return
    if (!/^https?:/i.test(url)) {
      window.alert('Only http and https image URLs are allowed.')
      return
    }
    exec('insertImage', url)
  }

  return (
    <div className="rich-editor">
      <div className="rich-editor-toolbar">
        <select
          className="rich-editor-block"
          onChange={(e) => applyBlock(e.target.value as Block)}
          defaultValue="p"
          aria-label="Text style"
          disabled={isSourceMode}
        >
          <option value="p">Normal</option>
          <option value="h1">Heading 1</option>
          <option value="h2">Heading 2</option>
          <option value="h3">Heading 3</option>
        </select>

        <span className="rich-editor-divider" />

        <ToolbarButton label="Bold" onClick={() => exec('bold')} disabled={isSourceMode}>
          <Bold size={14} />
        </ToolbarButton>
        <ToolbarButton label="Italic" onClick={() => exec('italic')} disabled={isSourceMode}>
          <Italic size={14} />
        </ToolbarButton>
        <ToolbarButton label="Underline" onClick={() => exec('underline')} disabled={isSourceMode}>
          <Underline size={14} />
        </ToolbarButton>

        <span className="rich-editor-divider" />

        <ToolbarButton label="Bulleted list" onClick={() => exec('insertUnorderedList')} disabled={isSourceMode}>
          <List size={14} />
        </ToolbarButton>
        <ToolbarButton label="Numbered list" onClick={() => exec('insertOrderedList')} disabled={isSourceMode}>
          <ListOrdered size={14} />
        </ToolbarButton>

        <span className="rich-editor-divider" />

        <ToolbarButton label="Insert link" onClick={insertLink} disabled={isSourceMode}>
          <Link2 size={14} />
        </ToolbarButton>
        <ToolbarButton label="Insert image" onClick={insertImage} disabled={isSourceMode}>
          <ImageIcon size={14} />
        </ToolbarButton>
        <ToolbarButton label="Clear formatting" onClick={() => exec('removeFormat')} disabled={isSourceMode}>
          <RemoveFormatting size={14} />
        </ToolbarButton>

        <span className="rich-editor-spacer" />

        <ToolbarButton
          label={isSourceMode ? 'Back to visual editing' : 'Edit HTML source'}
          onClick={() => setIsSourceMode((prev) => !prev)}
          active={isSourceMode}
        >
          <Code2 size={14} />
        </ToolbarButton>
      </div>

      {toolbarExtra}

      {isSourceMode ? (
        <textarea
          id={id}
          className="rich-editor-source"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          spellCheck={false}
          placeholder={placeholder}
        />
      ) : (
        <div
          id={id}
          ref={editorRef}
          className="rich-editor-surface"
          contentEditable
          suppressContentEditableWarning
          role="textbox"
          aria-multiline="true"
          data-placeholder={placeholder}
          onInput={(e) => onChange((e.target as HTMLDivElement).innerHTML)}
          onPaste={(e) => {
            // Paste as plain text by default. Pasting from Word or a webpage otherwise drags in
            // font tags and class names that the server then strips, so what an operator sees
            // while editing would not survive the save.
            e.preventDefault()
            const text = e.clipboardData.getData('text/plain')
            document.execCommand('insertText', false, text)
          }}
        />
      )}
    </div>
  )
}

const ToolbarButton: React.FC<{
  label: string
  onClick: () => void
  disabled?: boolean
  active?: boolean
  children: React.ReactNode
}> = ({ label, onClick, disabled, active, children }) => (
  <button
    type="button"
    className={`rich-editor-btn ${active ? 'active' : ''}`}
    // Buttons inside a form default to type="submit"; without onMouseDown prevention the editor
    // also loses its selection before the command runs.
    onMouseDown={(e) => e.preventDefault()}
    onClick={onClick}
    disabled={disabled}
    title={label}
    aria-label={label}
  >
    {children}
  </button>
)

export default RichTextEditor
