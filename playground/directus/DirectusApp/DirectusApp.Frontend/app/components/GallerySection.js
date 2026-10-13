"use client";

import Image from 'next/image';

export default function GallerySection({ tagline, headline, items = [] }) {
  if (!items || items.length === 0) {
    return null;
  }

  return (
    <section className="gallery-section">
      <div className="container">
        {tagline && <p className="tagline">{tagline}</p>}
        {headline && <h2>{headline}</h2>}

        <div className="gallery-grid">
          {items.map((item, index) => (
            <div className="gallery-item" key={index}>
              {item.directus_file?.id && (
                <Image
                  src={`http://localhost:8055/assets/${item.directus_file.id}`}
                  alt={item.directus_file.filename_download || 'Gallery image'}
                  width={400}
                  height={300}
                  className="gallery-image"
                />
              )}
            </div>
          ))}
        </div>
      </div>

      <style jsx>{`
        .gallery-section {
          padding: 80px 0;
          background-color: #f9fafb;
        }

        .container {
          max-width: 1200px;
          margin: 0 auto;
          padding: 0 20px;
          text-align: center;
        }

        .tagline {
          font-size: 18px;
          color: #718096;
          margin-bottom: 10px;
        }

        h2 {
          font-size: 36px;
          font-weight: bold;
          margin-bottom: 40px;
          color: #1a202c;
        }

        .gallery-grid {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
          gap: 30px;
          margin-top: 40px;
        }

        .gallery-item {
          background-color: #ffffff;
          border-radius: 8px;
          overflow: hidden;
          box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
          transition: transform 0.3s, box-shadow 0.3s;
        }

        .gallery-item:hover {
          transform: translateY(-5px);
          box-shadow: 0 10px 15px rgba(0, 0, 0, 0.1);
        }

        .gallery-image {
          width: 100%;
          height: auto;
          object-fit: cover;
        }

        @media (max-width: 768px) {
          .gallery-grid {
            grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
            gap: 20px;
          }
        }
      `}</style>
    </section>
  );
}
