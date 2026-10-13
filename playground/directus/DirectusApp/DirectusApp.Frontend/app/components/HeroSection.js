"use client";

import Image from 'next/image';
import Link from 'next/link';

export default function HeroSection({ tagline, headline, description, image, layout, button_group = [] }) {
  return (
    <section className="hero-section">
      <div className="container">
        {layout === 'image_left' && image && (
          <div className="hero-image">
            <Image
              src={`http://localhost:8055/assets/${image.id}`}
              alt={image.filename_download || 'Hero Image'}
              width={600}
              height={400}
              style={{ width: '100%', height: 'auto' }}
              priority
            />
          </div>
        )}

        <div className="hero-content">
          {tagline && <p className="tagline">{tagline}</p>}
          {headline && <h1>{headline}</h1>}
          {description && <p className="description">{description}</p>}

          {button_group.length > 0 && (
            <div className="button-group">
              {button_group.map((button, idx) => (
                <Link 
                  key={idx} 
                  href={resolveButtonUrl(button)}
                >
                  <button className={`cta-button ${button.variant || 'default'}`}>
                  {button.label}
                  </button>
                </Link>
              ))}
            </div>
          )}
        </div>

        {(layout === 'image_right' || layout === 'image_center' || layout === null) && image && (
          <div className="hero-image">
            <Image
              src={`http://localhost:8055/assets/${image.id}`}
              alt={image.filename_download || 'Hero Image'}
              width={600}
              height={400}
              priority
            />
          </div>
        )}
      </div>

      <style jsx>{`
        .hero-section {
          padding: 80px 0;
          background-color: #f5f7fa;
        }
        
        .container {
          max-width: 1200px;
          margin: 0 auto;
          padding: 0 20px;
          display: flex;
          align-items: center;
          justify-content: space-between;
          flex-wrap: wrap;
          flex-direction: ${layout === 'image_center' ? 'column' : 'row'};
          text-align: ${layout === 'image_center' ? 'center' : 'left'};
        }
        
        .hero-content {
          flex: 1;
          min-width: 300px;
          margin: ${layout === 'image_left' ? '0 0 0 40px' : '0 40px 0 0'};
        }
        
        h1 {
          font-size: 48px;
          font-weight: bold;
          margin-bottom: 20px;
          color: #1a202c;
        }
        
        .tagline {
          font-size: 18px;
          color: #718096;
          margin-bottom: 10px;
        }
        
        .description {
          font-size: 20px;
          line-height: 1.6;
          color: #4a5568;
          margin-bottom: 30px;
        }
        
        .button-group {
          display: flex;
          flex-wrap: wrap;
          gap: 12px;
        }
        
        .cta-button {
          display: inline-block;
          background-color: #3182ce;
          color: white;
          font-size: 18px;
          padding: 12px 30px;
          border-radius: 6px;
          text-decoration: none;
          font-weight: 600;
          transition: background-color 0.3s;
        }
        
        .cta-button:hover {
          background-color: #2b6cb0;
        }

        .cta-button.outline {
          background: transparent;
          border: 2px solid #3182ce;
          color: #3182ce;
        }

        .cta-button.soft {
          background: #ebf8ff;
          color: #3182ce;
        }

        .cta-button.ghost {
          background: transparent;
          color: #3182ce;
        }

        .cta-button.link {
          background: none;
          color: #3182ce;
          text-decoration: underline;
        }
        
        .hero-image {
          flex: 1;
          min-width: 300px;
          display: flex;
          justify-content: center;
          align-items: center;
        }
        
        @media (max-width: 768px) {
          .container {
            flex-direction: column;
          }
          
          .hero-content {
            margin: 0 0 30px 0;
          }
        }
      `}</style>
    </section>
  );
}

// Helper function to resolve button link
function resolveButtonUrl(button) {
  if (button.type === 'page' && button.page) return `${button.page.permalink}`;
  if (button.type === 'post' && button.post) return `/posts/${button.post.id}`;
  return button.url || '#';
}
