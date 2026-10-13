"use client";

export default function RichTextSection({ tagline, headline, content, alignment = 'center' }) {
  return (
    <section className="rich-text-section">
      <div className="container" style={{ textAlign: alignment }}>
        {tagline && <p className="tagline">{tagline}</p>}
        {headline && <h2>{headline}</h2>}
        {content && (
          <div className="content" dangerouslySetInnerHTML={{ __html: content }} />
        )}
      </div>

      <style jsx>{`
        .rich-text-section {
          padding: 80px 0;
          background-color: #ffffff;
        }
        
        .container {
          max-width: 900px;
          margin: 0 auto;
          padding: 0 20px;
        }
        
        .tagline {
          font-size: 18px;
          color: #718096;
          margin-bottom: 10px;
        }

        h2 {
          font-size: 36px;
          font-weight: bold;
          margin-bottom: 30px;
          color: #1a202c;
        }
        
        .content {
          font-size: 18px;
          line-height: 1.7;
          color: #4a5568;
        }
        
        .content p {
          margin-bottom: 20px;
        }
        
        .content h3 {
          font-size: 24px;
          margin-top: 40px;
          margin-bottom: 20px;
          color: #2d3748;
        }
        
        .content ul, .content ol {
          margin-bottom: 20px;
          padding-left: 20px;
        }
        
        .content li {
          margin-bottom: 10px;
        }
        
        @media (max-width: 768px) {
          h2 {
            font-size: 30px;
          }
          
          .content {
            font-size: 16px;
          }
        }
      `}</style>
    </section>
  );
}
